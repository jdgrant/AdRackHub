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
