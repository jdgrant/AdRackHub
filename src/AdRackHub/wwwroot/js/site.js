window.AdRackBillingTerm = (function () {
    const STANDARD = new Set(['1', '3', '4', '12']);

    function elements(root) {
        root = root || document;
        return {
            preset: root.querySelector('[data-billing-term-preset]'),
            monthsInput: root.querySelector('[data-billing-term-months]'),
            customGroup: root.querySelector('[data-billing-term-custom]')
        };
    }

    function showCustom(root, visible) {
        const { customGroup } = elements(root);
        if (customGroup) customGroup.hidden = !visible;
    }

    function applyMonthsToPreset(root) {
        const { preset, monthsInput } = elements(root);
        if (!preset || !monthsInput) return;

        const months = String(parseInt(monthsInput.value, 10) || '');
        if (STANDARD.has(months)) {
            preset.value = months;
            showCustom(root, false);
        } else {
            preset.value = 'custom';
            showCustom(root, true);
        }
    }

    function applyPresetToMonths(root) {
        const { preset, monthsInput } = elements(root);
        if (!preset || !monthsInput) return;
        if (preset.value === 'custom') {
            showCustom(root, true);
            monthsInput.focus();
            return;
        }
        monthsInput.value = preset.value;
        showCustom(root, false);
    }

    function setMonths(value, root) {
        const { monthsInput } = elements(root);
        if (!monthsInput) return;
        monthsInput.value = value;
        applyMonthsToPreset(root);
        if (!STANDARD.has(String(parseInt(value, 10) || ''))) {
            monthsInput.focus();
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-billing-term-preset]').forEach(function (preset) {
            const root = preset.closest('form') || document;
            applyMonthsToPreset(root);
            preset.addEventListener('change', function () {
                applyPresetToMonths(root);
            });
        });
    });

    return { setMonths };
})();

(function () {
    function bindInventoryEntry(form) {
        const casesInput = form.querySelector('[data-cases]');
        const caseCountInput = form.querySelector('[data-case-count]');
        const totalInput = form.querySelector('[data-total]');
        if (!casesInput || !caseCountInput || !totalInput) return;
        function recalc() {
            const cases = parseFloat(casesInput.value);
            const caseCount = parseInt(caseCountInput.value, 10);
            if (!Number.isNaN(cases) && !Number.isNaN(caseCount) && cases >= 0 && caseCount >= 0) {
                totalInput.value = String(Math.round(cases * caseCount));
            }
        }
        casesInput.addEventListener('input', recalc);
        caseCountInput.addEventListener('input', recalc);

        const kind = form.querySelector('[name="kind"]');
        const receipt = form.querySelector('[data-receipt-option]');
        if (kind && receipt)
        {
            const syncReceipt = function () {
                const received = kind.value === 'Received';
                receipt.classList.toggle('d-none', !received);
                const box = receipt.querySelector('[name="sendReceipt"]');
                if (box && !received)
                    box.checked = false;
            };
            kind.addEventListener('change', syncReceipt);
            syncReceipt();
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-inventory-entry]').forEach(bindInventoryEntry);
    });
})();

(function () {
    const modalEl = document.getElementById('duplicateContractModal');
    if (!modalEl || !window.bootstrap) return;

    const modal = window.bootstrap.Modal.getOrCreateInstance(modalEl);
    const idInput = document.getElementById('duplicateContractId');
    const nameInput = document.getElementById('duplicateContractName');
    const percentInput = document.getElementById('duplicateIncreasePercent');
    const proposalInput = document.getElementById('duplicateIsProposal');
    const nameNote = document.getElementById('duplicateNameNote');
    const routeList = document.getElementById('duplicateRouteList');
    const totalEl = document.getElementById('duplicateMonthlyTotal');
    const submitButton = document.getElementById('duplicateSubmitButton');
    const titleEl = document.getElementById('duplicateContractTitle');
    const termEl = document.getElementById('duplicateTerm');
    let preview = null;
    let nameEdited = false;

    function money(amount, whole) {
        return amount.toLocaleString('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: whole ? 0 : 2,
            maximumFractionDigits: whole ? 0 : 2
        });
    }

    function percentValue() {
        const value = parseFloat(percentInput.value);
        return Number.isFinite(value) ? value : 0;
    }

    function pricedMonthly(monthly) {
        const percent = percentValue();
        if (!percent) return { amount: monthly, whole: false };
        return { amount: Math.round(monthly * (1 + percent / 100)), whole: true };
    }

    function suggestedName() {
        if (!preview) return '';
        return proposalInput.checked ? preview.proposalName : preview.copyName;
    }

    function syncNameNote() {
        if (!preview || !preview.nameTaken || nameEdited || nameInput.value.trim() !== preview.contractName) {
            nameNote.classList.add('d-none');
            nameNote.textContent = '';
            return;
        }
        nameNote.textContent = 'This name is already used, so it will be saved as ' + suggestedName() + ' unless you type a different one.';
        nameNote.classList.remove('d-none');
    }

    function renderPrices() {
        if (!preview) return;
        let total = 0;
        routeList.querySelectorAll('[data-monthly]').forEach(function (row) {
            const monthly = parseFloat(row.getAttribute('data-monthly'));
            const priced = pricedMonthly(monthly);
            total += priced.amount;
            const price = row.querySelector('[data-route-price]');
            if (price) price.textContent = money(priced.amount, priced.whole) + ' / month';
        });
        totalEl.textContent = money(total, percentValue() !== 0);
    }

    function renderPreview() {
        titleEl.textContent = preview.contractName;
        if (!nameEdited) nameInput.value = preview.contractName;
        const begins = preview.startLabel ? 'Begins ' + preview.startLabel + '.' : 'No beginning date.';
        const ends = preview.endLabel ? ' Ends ' + preview.endLabel + '.' : ' Does not end.';
        termEl.textContent = begins + ends;
        routeList.replaceChildren();
        if (!preview.routes.length) {
            routeList.textContent = 'No routes on this contract.';
        } else {
            preview.routes.forEach(function (route) {
                const row = document.createElement('div');
                row.className = 'border-top py-2';
                row.setAttribute('data-monthly', String(route.monthly));
                const heading = document.createElement('div');
                heading.className = 'd-flex justify-content-between gap-3';
                const name = document.createElement('div');
                name.className = 'fw-semibold';
                name.textContent = route.routeName;
                const price = document.createElement('div');
                price.setAttribute('data-route-price', '');
                heading.append(name, price);
                const locations = document.createElement('div');
                locations.className = 'text-muted small';
                locations.textContent = route.locations && route.locations.length
                    ? route.locations.join(', ')
                    : 'No locations on this route.';
                row.append(heading, locations);
                routeList.append(row);
            });
        }
        submitButton.textContent = proposalInput.checked ? 'Create proposal' : 'Create contract';
        syncNameNote();
        renderPrices();
    }

    async function openDuplicate(contractId) {
        preview = null;
        nameEdited = false;
        idInput.value = contractId;
        percentInput.value = '0';
        proposalInput.checked = true;
        nameInput.value = '';
        termEl.textContent = '';
        nameNote.classList.add('d-none');
        routeList.textContent = 'Loading…';
        totalEl.textContent = '';
        submitButton.disabled = true;
        modal.show();
        try {
            const response = await fetch('/CustomerContracts/DuplicatePreview/' + contractId, { headers: { 'Accept': 'application/json' } });
            if (!response.ok) throw new Error('Could not load this contract.');
            preview = await response.json();
            submitButton.disabled = false;
            renderPreview();
        } catch (error) {
            routeList.textContent = error.message || 'Could not load this contract.';
        }
    }

    document.querySelectorAll('[data-duplicate-contract]').forEach(function (button) {
        button.addEventListener('click', function () {
            openDuplicate(button.getAttribute('data-contract-id'));
        });
    });

    percentInput.addEventListener('input', renderPrices);
    proposalInput.addEventListener('change', function () {
        submitButton.textContent = proposalInput.checked ? 'Create proposal' : 'Create contract';
        syncNameNote();
    });
    nameInput.addEventListener('input', function () {
        nameEdited = true;
        syncNameNote();
    });
    document.getElementById('duplicateContractForm').addEventListener('submit', function () {
        if (!preview || nameEdited) return;
        if (preview.nameTaken && nameInput.value.trim() === preview.contractName)
            nameInput.value = suggestedName();
    });
})();
