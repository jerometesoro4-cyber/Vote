"""Local HTTP smoke test using VOTE_TEST_* credentials and SQLCMD.
Set VOTE_TEST_DB to match the test app's SQL Server database (default ACMCLVoting).
The test removes its two uniquely named elections in finally.
"""
from datetime import datetime, timedelta, timezone
import os
import re
import subprocess
import uuid

import requests

BASE = os.environ.get('VOTE_TEST_URL', 'http://127.0.0.1:5189')
DB = os.environ.get('VOTE_TEST_DB', 'ACMCLVoting')
STANDARD_POSITIONS = ['President', 'Vice President', 'Secretary', 'Treasurer', 'Auditor']


def sql(query):
    return subprocess.check_output(
        ['sqlcmd', '-b', '-S', 'localhost', '-E', '-C', '-d', DB, '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query],
        text=True,
    ).strip()


def page(session, path):
    response = session.get(BASE + path, timeout=15)
    assert response.status_code == 200, (path, response.status_code)
    return response.text


def post(session, action, source, data):
    markup = page(session, source)
    token = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', markup).group(1)
    fields = [('__RequestVerificationToken', token)]
    fields.extend(data.items() if isinstance(data, dict) else data)
    return session.post(BASE + action, data=fields, timeout=15, allow_redirects=False)


def assert_redirect(response, target):
    assert response.status_code == 302 and response.headers.get('Location') == target, (
        response.status_code, response.headers.get('Location'), target,
    )


def login(role):
    session = requests.Session()
    response = post(session, '/Account/Login', '/Account/Login', {
        'email': os.environ[f'VOTE_TEST_{role.upper()}_EMAIL'],
        'password': os.environ[f'VOTE_TEST_{role.upper()}_PASSWORD'],
    })
    assert_redirect(response, f'/{role}/Index')
    return session


def create_election(name, starts, ends, standard):
    fields = [
        ('name', name),
        ('startsAtUtc', starts.strftime('%Y-%m-%dT%H:%M')),
        ('endsAtUtc', ends.strftime('%Y-%m-%dT%H:%M')),
        *([('includeStandardPositions', 'true')] if standard else []),
        ('includeStandardPositions', 'false'),
    ]
    response = post(admin, '/Admin/CreateElection', '/Admin/Elections', fields)
    election_id = int(sql(f"SELECT Id FROM Elections WHERE Name='{name}'"))
    assert_redirect(response, f'/Admin/Elections/{election_id}')
    return election_id


def candidates_for(election_id, name):
    path = f'/Admin/Candidates/{election_id}'
    choices = {}
    for value in sql(f'SELECT Id FROM Positions WHERE ElectionId={election_id}').split():
        position_id = int(value)
        response = post(admin, '/Admin/AddCandidate', path, {
            'electionId': election_id, 'positionId': position_id, 'name': name, 'platform': 'Test fixture',
        })
        assert_redirect(response, path)
        choices[f'choices[{position_id}]'] = int(sql(f'SELECT Id FROM Candidates WHERE PositionId={position_id}'))
    return choices


admin = login('Admin')
student = login('Student')
prefix = 'SMOKE-' + uuid.uuid4().hex
room101 = prefix + ' Room 101'
room102 = prefix + ' Room 102'

try:
    for action in ['Index', 'Elections', 'Positions', 'Candidates', 'Users', 'Monitoring', 'Results', 'Settings']:
        page(admin, f'/Admin/{action}')
    assert student.get(BASE + '/Admin/Index', allow_redirects=False).status_code == 302
    assert admin.post(BASE + '/Admin/CreateElection', data={'name': 'csrf'}).status_code == 400

    now = datetime.now(timezone.utc)
    first = create_election(room101, now - timedelta(hours=1), now + timedelta(hours=2), True)
    other = create_election(room102, now + timedelta(hours=1), now + timedelta(hours=2), False)
    setup = f'/Admin/Elections/{first}'
    positions = f'/Admin/Positions/{first}'
    assert sql(f'SELECT Name FROM Positions WHERE ElectionId={first} ORDER BY Id').splitlines() == STANDARD_POSITIONS
    assert sql(f'SELECT COUNT(*) FROM Positions WHERE ElectionId={other}') == '0'
    assert f'href="{setup}"' in page(admin, '/Admin/Elections')
    assert f'href="/Admin/Candidates/{first}"' in page(admin, positions)
    assert_redirect(post(admin, '/Admin/AddPosition', positions, {'electionId': first, 'name': 'Room Representative'}), positions)
    assert 'Room Representative' in page(admin, positions)
    assert_redirect(post(admin, '/Admin/ChangeStatus', setup, {'id': first, 'status': 'Open'}), setup)
    assert 'one candidate for each position' in page(admin, setup)

    other_positions = f'/Admin/Positions/{other}'
    assert_redirect(post(admin, '/Admin/AddPosition', other_positions, {'electionId': other, 'name': 'president'}), other_positions)
    for _ in range(2):
        assert_redirect(post(admin, '/Admin/AddPosition', other_positions, {'electionId': other, 'addStandard': 'true'}), other_positions)
    assert sql(f'SELECT COUNT(*) FROM Positions WHERE ElectionId={other}') == str(len(STANDARD_POSITIONS))
    assert student.get(BASE + f'/Student/Vote/{first}').status_code == 404

    first_choices = candidates_for(first, 'First Room Candidate')
    fields = {'electionId': first, **first_choices}
    other_choices = candidates_for(other, 'Second Room Candidate')
    other_position_id = int(sql(f'SELECT MIN(Id) FROM Positions WHERE ElectionId={other}'))
    admin_candidates = f'/Admin/Candidates/{first}'
    assert_redirect(post(admin, '/Admin/AddCandidate', admin_candidates, {
        'electionId': first, 'positionId': other_position_id, 'name': 'Mismatched Room Candidate',
    }), admin_candidates)
    scoped_admin = page(admin, admin_candidates)
    assert 'First Room Candidate' in scoped_admin and 'Second Room Candidate' not in scoped_admin
    assert sql("SELECT COUNT(*) FROM Candidates WHERE Name='Mismatched Room Candidate' "
               f'AND PositionId={other_position_id}') == '0'

    assert_redirect(post(admin, '/Admin/ChangeStatus', setup, {'id': first, 'status': 'Open'}), setup)
    other_setup = f'/Admin/Elections/{other}'
    assert_redirect(post(admin, '/Admin/ChangeStatus', other_setup, {'id': other, 'status': 'Open'}), other_setup)
    ballot = f'/Student/Vote/{first}'
    other_ballot = f'/Student/Vote/{other}'
    directory = f'/Student/Candidates/{first}'
    other_directory = f'/Student/Candidates/{other}'
    for path in ['/Student/Index', '/Student/ActiveElections']:
        assert f'href="{ballot}"' in page(student, path)
    assert 'Scheduled' in page(student, other_ballot) and 'Submit final ballot' not in page(student, other_ballot)
    assert other_directory not in page(student, '/Student/Candidates')
    assert_redirect(student.get(BASE + other_directory, allow_redirects=False), '/Student/Candidates')
    assert_redirect(post(student, '/Student/Vote', other_ballot, {'electionId': other, **other_choices}), other_ballot)
    assert 'not currently open' in page(student, other_ballot)

    sql(f'UPDATE Elections SET StartsAtUtc=DATEADD(hour,-1,SYSUTCDATETIME()) WHERE Id={other}')
    choices_page = page(student, '/Student/Candidates')
    assert f'href="{directory}"' in choices_page and f'href="{other_directory}"' in choices_page
    assert 'First Room Candidate' not in choices_page and 'Second Room Candidate' not in choices_page
    scoped_student = page(student, directory)
    assert 'First Room Candidate' in scoped_student and 'Second Room Candidate' not in scoped_student
    assert f'href="{ballot}"' in scoped_student

    ballot_screen = page(student, ballot)
    assert 'Submit final ballot' in ballot_screen and f'href="{directory}"' in ballot_screen
    assert 'Second Room Candidate' not in ballot_screen
    assert ballot_screen.count('name="choices[') == len(STANDARD_POSITIONS) + 1
    first_choice, second_choice = list(first_choices)[:2]
    wrong_room = {**fields, first_choice: next(iter(other_choices.values()))}
    assert_redirect(post(student, '/Student/Vote', ballot, wrong_room), ballot)
    assert 'Select one valid candidate' in page(student, ballot)
    swapped = {**fields, first_choice: fields[second_choice], second_choice: fields[first_choice]}
    assert_redirect(post(student, '/Student/Vote', ballot, swapped), ballot)
    assert 'Select one valid candidate' in page(student, ballot)
    assert sql(f'SELECT COUNT(*) FROM Ballots WHERE ElectionId IN ({first}, {other})') == '0'

    assert_redirect(post(student, '/Student/Vote', ballot, fields), '/Student/History')
    history = page(student, '/Student/History')
    assert room101 in history and 'Your ballot has been submitted.' in history
    submitted = page(student, ballot)
    assert 'Ballot submitted' in submitted and 'Submit final ballot' not in submitted
    assert_redirect(post(student, '/Student/Vote', ballot, fields), ballot)
    assert sql(f'SELECT COUNT(*) FROM Ballots WHERE ElectionId={first}') == '1'
    assert room101 not in page(student, '/PublicPortal/Results')

    assert_redirect(post(admin, '/Admin/ChangeStatus', setup, {'id': first, 'status': 'Closed'}), setup)
    assert directory not in page(student, '/Student/Candidates')
    assert room101 in page(student, '/PublicPortal/Results')
    assert room101 in page(student, '/Student/Results')
    print('PASS: default and custom positions, room context and candidate isolation, voting schedule, ballot submission, duplicate rejection, and results.')
finally:
    sql(f"""
        BEGIN TRANSACTION;
        DECLARE @fixtures TABLE (Id int);
        INSERT INTO @fixtures SELECT Id FROM Elections WHERE Name IN ('{room101}', '{room102}');
        DELETE FROM BallotSelections WHERE BallotId IN (
            SELECT Id FROM Ballots WHERE ElectionId IN (SELECT Id FROM @fixtures));
        DELETE FROM Ballots WHERE ElectionId IN (SELECT Id FROM @fixtures);
        DELETE FROM Candidates WHERE PositionId IN (
            SELECT Id FROM Positions WHERE ElectionId IN (SELECT Id FROM @fixtures));
        DELETE FROM Positions WHERE ElectionId IN (SELECT Id FROM @fixtures);
        DELETE FROM Elections WHERE Id IN (SELECT Id FROM @fixtures);
        COMMIT;
    """)
