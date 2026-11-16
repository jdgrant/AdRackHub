window.AdRackCustomerMap = (function () {
    var map;
    var stopLayer;
    var customerMarker;

    function pinIcon(color, kind) {
        var isProspect = kind === 'prospect';
        var size = isProspect ? 20 : 18;
        var span = isProspect
            ? '<span style="background:' + color + ';width:16px;height:16px;border-radius:50%;transform:none;border:2px solid #212529;"></span>'
            : '<span style="background:' + color + ';"></span>';
        return L.divIcon({
            className: 'overview-map-pin',
            html: span,
            iconSize: [size, size],
            iconAnchor: [size / 2, size],
            popupAnchor: [0, -16]
        });
    }

    function init(options) {
        var el = document.getElementById(options.mapId);
        if (!el || typeof L === 'undefined')
            return;

        var lat = parseFloat(el.getAttribute('data-lat'));
        var lng = parseFloat(el.getAttribute('data-lng'));
        var hasCustomer = isFinite(lat) && isFinite(lng);
        var name = el.getAttribute('data-name') || '';
        var address = el.getAttribute('data-address') || '';
        var started = false;
        var select = options.contractSelectId
            ? document.getElementById(options.contractSelectId)
            : null;
        var stage = options.stageId ? document.getElementById(options.stageId) : null;
        var fullscreenButton = options.fullscreenButtonId
            ? document.getElementById(options.fullscreenButtonId)
            : null;

        function start() {
            if (started) {
                if (map)
                    map.invalidateSize();
                return;
            }
            started = true;

            map = L.map(el);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            }).addTo(map);
            stopLayer = L.layerGroup().addTo(map);

            if (hasCustomer) {
                var popup = '<strong>' + escapeHtml(name) + '</strong>';
                if (address)
                    popup += '<br>' + escapeHtml(address);
                popup += '<br><span class="text-muted">Customer</span>';
                customerMarker = L.marker([lat, lng]).addTo(map).bindPopup(popup);
            }

            plotSelected();
            setTimeout(function () {
                if (map)
                    map.invalidateSize();
            }, 150);
        }

        function selectedContractId() {
            return select ? select.value : '';
        }

        function plotSelected() {
            if (!map || !stopLayer)
                return;

            stopLayer.clearLayers();
            var bounds = L.latLngBounds([]);
            var panel = document.querySelector('.map-contract-stops[data-contract-id="' + selectedContractId() + '"]');
            if (panel) {
                panel.querySelectorAll('.map-stop').forEach(function (row) {
                    var stopLat = parseFloat(row.getAttribute('data-lat'));
                    var stopLng = parseFloat(row.getAttribute('data-lng'));
                    if (!isFinite(stopLat) || !isFinite(stopLng))
                        return;

                    var label = row.getAttribute('data-name') || 'Stop';
                    var step = row.getAttribute('data-step');
                    var route = row.getAttribute('data-route') || '';
                    var stopAddress = row.getAttribute('data-address') || '';
                    var color = row.getAttribute('data-color') || '#4363d8';
                    var kind = row.getAttribute('data-kind') || 'stop';
                    var url = row.getAttribute('data-url') || '';
                    var html = '<strong>' + escapeHtml(step ? '#' + step + ' ' + label : label) + '</strong>';
                    if (kind === 'prospect')
                        html += '<br><span class="text-muted">Prospect hotel</span>';
                    else if (route)
                        html += '<br><span class="text-muted">' + escapeHtml(route) + '</span>';
                    if (stopAddress && stopAddress !== '—')
                        html += '<br>' + escapeHtml(stopAddress);
                    if (url)
                        html += '<br><a href="' + escapeHtml(url) + '">Details</a>';
                    L.marker([stopLat, stopLng], {
                        icon: pinIcon(color, kind),
                        zIndexOffset: kind === 'prospect' ? 400 : 250,
                        title: label
                    }).addTo(stopLayer).bindPopup(html);
                    bounds.extend([stopLat, stopLng]);
                });
            }

            if (bounds.isValid())
                map.fitBounds(bounds, { padding: [24, 24], maxZoom: 12 });
            else if (hasCustomer)
                map.setView([lat, lng], 14);

            if (customerMarker)
                customerMarker.bringToFront();
        }

        function showContract(contractId) {
            document.querySelectorAll('.map-contract-stops, .customer-map-legend').forEach(function (panel) {
                panel.classList.toggle('d-none', panel.getAttribute('data-contract-id') !== contractId);
            });
            if (started)
                plotSelected();
        }

        function fullscreenActive() {
            return document.fullscreenElement === stage
                || (stage && stage.classList.contains('is-fullscreen'));
        }

        function syncFullscreenButton() {
            if (!fullscreenButton)
                return;
            fullscreenButton.textContent = fullscreenActive() ? 'Exit full screen' : 'Full screen';
        }

        function resizeSoon() {
            setTimeout(function () {
                if (map)
                    map.invalidateSize();
            }, 60);
        }

        function enterFullscreen() {
            if (stage)
                stage.classList.add('is-fullscreen');
            document.body.classList.add('customer-map-open');
            syncFullscreenButton();
            resizeSoon();
            if (stage && stage.requestFullscreen && document.fullscreenElement !== stage) {
                var pending = stage.requestFullscreen();
                if (pending && pending.catch)
                    pending.catch(function () { });
            }
        }

        function exitFullscreen() {
            if (document.fullscreenElement && document.exitFullscreen)
                document.exitFullscreen().catch(function () { });
            if (stage)
                stage.classList.remove('is-fullscreen');
            document.body.classList.remove('customer-map-open');
            syncFullscreenButton();
            resizeSoon();
        }

        if (select) {
            select.addEventListener('change', function () {
                showContract(select.value);
            });
        }

        if (fullscreenButton) {
            fullscreenButton.addEventListener('click', function () {
                if (fullscreenActive())
                    exitFullscreen();
                else
                    enterFullscreen();
            });
        }

        document.addEventListener('fullscreenchange', function () {
            if (!document.fullscreenElement && stage)
                stage.classList.remove('is-fullscreen');
            if (!document.fullscreenElement)
                document.body.classList.remove('customer-map-open');
            syncFullscreenButton();
            resizeSoon();
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' && stage && stage.classList.contains('is-fullscreen') && !document.fullscreenElement)
                exitFullscreen();
        });

        var pane = document.getElementById(options.tabPaneId || 'tab-map');
        if (pane && pane.classList.contains('active')) {
            start();
            enterFullscreen();
        }

        var tabBtn = document.getElementById(options.tabButtonId || 'tab-map-btn');
        if (tabBtn) {
            tabBtn.addEventListener('shown.bs.tab', function () {
                start();
                enterFullscreen();
            });
        }
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    return { init: init };
})();
