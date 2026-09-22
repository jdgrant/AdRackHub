window.AdRackCustomerMap = (function () {
    var map;

    function init(options) {
        var el = document.getElementById(options.mapId);
        if (!el || typeof L === 'undefined')
            return;

        var lat = parseFloat(el.getAttribute('data-lat'));
        var lng = parseFloat(el.getAttribute('data-lng'));
        if (!isFinite(lat) || !isFinite(lng))
            return;

        var name = el.getAttribute('data-name') || '';
        var address = el.getAttribute('data-address') || '';
        var started = false;

        function start() {
            if (started) {
                if (map)
                    map.invalidateSize();
                return;
            }
            started = true;

            map = L.map(el).setView([lat, lng], 14);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
            }).addTo(map);

            var popup = '<strong>' + escapeHtml(name) + '</strong>';
            if (address)
                popup += '<br>' + escapeHtml(address);
            popup += '<br><span class="text-muted">' + lat.toFixed(5) + ', ' + lng.toFixed(5) + '</span>';
            L.marker([lat, lng]).addTo(map).bindPopup(popup).openPopup();

            setTimeout(function () {
                if (map)
                    map.invalidateSize();
            }, 150);
        }

        var pane = document.getElementById(options.tabPaneId || 'tab-map');
        if (pane && pane.classList.contains('active'))
            start();

        var tabBtn = document.getElementById(options.tabButtonId || 'tab-map-btn');
        if (tabBtn)
            tabBtn.addEventListener('shown.bs.tab', start);
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
