for (const position of document.querySelectorAll('.ballot-position')) {
    const choices = Array.from(position.querySelectorAll('input[type="checkbox"]'));
    if (choices.length === 0) continue;

    const minimum = Number(position.dataset.minSelections);
    const maximum = Number(position.dataset.maxSelections);
    const message = position.querySelector('.ballot-instruction').textContent.trim();

    function validate() {
        const count = choices.filter(choice => choice.checked).length;
        choices[0].setCustomValidity(count < minimum || count > maximum ? message : '');
    }

    position.addEventListener('change', validate);
    window.addEventListener('pageshow', validate);
    validate();
}

for (const toggle of document.querySelectorAll('[data-password-toggle]')) {
    const targetId = toggle.getAttribute('aria-controls');
    const input = targetId ? document.getElementById(targetId) : null;
    if (!input) continue;

    const showLabel = toggle.getAttribute('aria-label') || 'Show password';
    const hideLabel = showLabel.replace(/^Show\b/i, 'Hide');

    toggle.addEventListener('click', () => {
        const reveal = input.type === 'password';
        input.type = reveal ? 'text' : 'password';
        toggle.textContent = reveal ? 'Hide' : 'Show';
        toggle.setAttribute('aria-pressed', String(reveal));
        toggle.setAttribute('aria-label', reveal ? hideLabel : showLabel);
    });
}

if (!('commandForElement' in HTMLButtonElement.prototype)) {
    document.addEventListener('click', (event) => {
        const trigger = event.target.closest('button[command][commandfor]');
        if (!trigger) return;
        const target = document.getElementById(trigger.getAttribute('commandfor'));
        if (!target || target.tagName !== 'DIALOG') return;
        const cmd = trigger.getAttribute('command');
        if (cmd === 'show-modal' && typeof target.showModal === 'function' && !target.open) {
            target.showModal();
        } else if (cmd === 'close' && typeof target.close === 'function' && target.open) {
            target.close('cancel');
        }
    });
}

for (const dialog of document.querySelectorAll('dialog')) {
    dialog.addEventListener('close', () => {
        dialog.returnValue = '';
    });
}

for (const printButton of document.querySelectorAll('[data-print-report]')) {
    printButton.addEventListener('click', () => {
        window.print();
    });
}
