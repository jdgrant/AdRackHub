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

window.AdRackCaseWeight = (function () {
    function toPounds(value, unit) {
        const n = parseFloat(String(value ?? '').trim());
        if (!Number.isFinite(n) || n <= 0) return null;
        return unit === 'oz' ? n / 16 : n;
    }

    function formatWeight(pounds) {
        const oz = pounds * 16;
        if (pounds >= 1) {
            return pounds.toLocaleString(undefined, { maximumFractionDigits: 3 }) + ' lb';
        }
        return oz.toLocaleString(undefined, { maximumFractionDigits: 2 }) + ' oz';
    }

    function estimateTare(lengthIn, widthIn, heightIn, poundsPerMsf, joint) {
        const sheetLength = 2 * (lengthIn + widthIn) + joint;
        const sheetWidth = heightIn + widthIn;
        return (sheetLength * sheetWidth / 144) * poundsPerMsf / 1000;
    }

    function readBoards(root) {
        try {
            return JSON.parse(root.getAttribute('data-boards') || '[]');
        } catch (error) {
            return [];
        }
    }

    function calculate(root) {
        const joint = parseFloat(root.getAttribute('data-joint')) || 1.5;
        const sampleCount = parseInt(root.querySelector('[data-sample-count]').value, 10);
        const samplePounds = toPounds(
            root.querySelector('[data-sample-weight]').value,
            root.querySelector('[data-sample-unit]').value);
        const grossPounds = toPounds(
            root.querySelector('[data-gross-weight]').value,
            root.querySelector('[data-gross-unit]').value);
        const cases = parseInt(root.querySelector('[data-case-qty]').value, 10);
        const measured = toPounds(
            root.querySelector('[data-tare]').value,
            root.querySelector('[data-tare-unit]').value);
        const lengthIn = parseFloat(root.querySelector('[data-length]').value);
        const widthIn = parseFloat(root.querySelector('[data-width]').value);
        const heightIn = parseFloat(root.querySelector('[data-height]').value);
        const boardId = root.querySelector('[data-board]').value;
        const boards = readBoards(root);
        const board = boards.find(function (item) { return item.id === boardId; });
        let poundsPerMsf = board && board.lb != null ? Number(board.lb) : null;
        if (boardId === 'custom') {
            poundsPerMsf = parseFloat(root.querySelector('[data-custom-msf]').value);
        }

        if (!Number.isFinite(sampleCount) || sampleCount <= 0)
            return { ok: false, error: 'Enter how many brochures you weighed.' };
        if (samplePounds == null)
            return { ok: false, error: 'Enter the weight of those brochures.' };
        if (grossPounds == null)
            return { ok: false, error: 'Enter the full case weight.' };
        if (!Number.isFinite(cases) || cases <= 0)
            return { ok: false, error: 'Enter at least one case.' };

        const eachPounds = samplePounds / sampleCount;
        let tarePounds;
        let tareEstimated = false;
        if (measured != null) {
            tarePounds = measured;
        } else if (lengthIn > 0 && widthIn > 0 && heightIn > 0 && poundsPerMsf > 0) {
            tarePounds = estimateTare(lengthIn, widthIn, heightIn, poundsPerMsf, joint);
            tareEstimated = true;
        } else {
            return { ok: false, error: 'Enter the box length, width, and height in inches, or the weight of an empty box.' };
        }

        const netPounds = grossPounds - tarePounds;
        if (netPounds <= 0)
            return { ok: false, error: 'The case weighs less than the box. Check the weights or the box size.' };
        if (netPounds < eachPounds)
            return { ok: false, error: 'That sample is heavier than the brochures in the case. Check the weights.' };

        const unrounded = netPounds / eachPounds;
        const caseCount = Math.round(unrounded);
        if (caseCount <= 0)
            return { ok: false, error: 'The case is lighter than one brochure. Check the weights.' };

        const total = cases * caseCount;
        const drift = Math.abs(unrounded - caseCount);
        return {
            ok: true,
            eachPounds: eachPounds,
            tarePounds: tarePounds,
            tareEstimated: tareEstimated,
            netPounds: netPounds,
            unrounded: unrounded,
            caseCount: caseCount,
            cases: cases,
            total: total,
            warning: drift > 0.35
                ? 'The weight works out to ' + unrounded.toFixed(1) + ' brochures, rounded to ' + caseCount.toLocaleString() + '. If that looks off, weigh an empty box or a larger sample.'
                : ''
        };
    }

    function setInput(input, value) {
        if (!input) return;
        input.value = String(value);
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }

    function bind(root) {
        root.addEventListener('keydown', function (event) {
            if (event.key === 'Enter') event.preventDefault();
        });

        const hint = root.querySelector('[data-hint]');
        const resultBox = root.querySelector('[data-result]');
        const errorBox = root.querySelector('[data-out-error]');
        const warningBox = root.querySelector('[data-out-warning]');
        const applyButtons = root.querySelectorAll('[data-apply-row], [data-apply-kind]');
        const customBoard = root.querySelector('[data-custom-board]');
        let latest = null;

        const target = root.querySelector('[data-target-row]');
        if (target && root.getAttribute('data-apply') === 'inventory') {
            document.querySelectorAll('#inventory-table [data-inventory-row]').forEach(function (row) {
                const option = document.createElement('option');
                option.value = row.getAttribute('data-row-index') || '';
                option.textContent = row.getAttribute('data-label') || 'Customer';
                target.appendChild(option);
            });
        }

        function paint() {
            const started = String(root.querySelector('[data-sample-weight]').value || '').trim() !== ''
                || String(root.querySelector('[data-gross-weight]').value || '').trim() !== '';
            latest = started ? calculate(root) : null;
            const ok = !!(latest && latest.ok);
            applyButtons.forEach(function (button) { button.disabled = !ok; });
            if (!latest) {
                hint.classList.remove('d-none');
                resultBox.classList.add('d-none');
                errorBox.classList.add('d-none');
                return;
            }
            hint.classList.add('d-none');
            if (!latest.ok) {
                resultBox.classList.add('d-none');
                errorBox.textContent = latest.error;
                errorBox.classList.remove('d-none');
                return;
            }
            errorBox.classList.add('d-none');
            resultBox.classList.remove('d-none');
            root.querySelector('[data-out-each]').textContent = formatWeight(latest.eachPounds);
            root.querySelector('[data-out-tare]').textContent = formatWeight(latest.tarePounds) + (latest.tareEstimated ? ' estimated' : ' measured');
            root.querySelector('[data-out-count]').textContent = latest.caseCount.toLocaleString();
            root.querySelector('[data-out-cases]').textContent = latest.cases.toLocaleString();
            root.querySelector('[data-out-total]').textContent = latest.total.toLocaleString();
            if (latest.warning) {
                warningBox.textContent = latest.warning;
                warningBox.classList.remove('d-none');
            } else {
                warningBox.classList.add('d-none');
            }
        }

        root.querySelector('[data-board]').addEventListener('change', function () {
            customBoard.classList.toggle('d-none', root.querySelector('[data-board]').value !== 'custom');
            paint();
        });
        root.querySelectorAll('input, select').forEach(function (field) {
            field.addEventListener('input', paint);
            field.addEventListener('change', paint);
        });

        const applyRow = root.querySelector('[data-apply-row]');
        if (applyRow) {
            applyRow.addEventListener('click', function () {
                if (!latest || !latest.ok) return;
                const index = target ? target.value : '';
                const row = document.querySelector('#inventory-table [data-inventory-row][data-row-index="' + index + '"]');
                if (!row) {
                    errorBox.textContent = 'Choose a customer on this warehouse.';
                    errorBox.classList.remove('d-none');
                    return;
                }
                setInput(row.querySelector('[data-cases]'), latest.cases);
                setInput(row.querySelector('[data-case-count]'), latest.caseCount);
                setInput(row.querySelector('[data-total]'), latest.total);
                row.scrollIntoView({ block: 'center' });
            });
        }

        root.querySelectorAll('[data-apply-kind]').forEach(function (button) {
            button.addEventListener('click', function () {
                if (!latest || !latest.ok) return;
                const kind = document.getElementById('inventoryKind');
                const casesInput = document.getElementById('inventoryCases');
                const caseCountInput = document.getElementById('inventoryCaseCount');
                const totalInput = document.getElementById('quantity');
                if (kind) kind.value = button.getAttribute('data-apply-kind') || 'Count';
                setInput(casesInput, latest.cases);
                setInput(caseCountInput, latest.caseCount);
                setInput(totalInput, latest.total);
                document.getElementById('inventory-log-form')?.scrollIntoView({ block: 'center' });
            });
        });
    }

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
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-case-calculator]').forEach(bind);
        document.querySelectorAll('[data-inventory-entry]').forEach(bindInventoryEntry);
    });
})();
