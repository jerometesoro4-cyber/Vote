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
