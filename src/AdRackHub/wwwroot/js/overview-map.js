window.AdRackOverviewMap = (function () {
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

    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function popupHtml(marker) {
        var html = '<strong>' + escapeHtml(marker.name) + '</strong>';
        if (marker.kind === 'prospect')
            html += '<br><span class="text-muted">Prospect hotel</span>';
        else if (marker.routeName)
            html += '<br><span class="text-muted">' + escapeHtml(marker.routeName) + '</span>';
        if (marker.address && marker.address !== '—')
            html += '<br>' + escapeHtml(marker.address);
        if (marker.detailsUrl)
            html += '<br><a href="' + escapeHtml(marker.detailsUrl) + '">Details</a>';
        return html;
    }

    function median(values) {
        var sorted = values.slice().sort(function (a, b) { return a - b; });
        var mid = Math.floor(sorted.length / 2);
        return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    function haversineMiles(lat1, lng1, lat2, lng2) {
        var toRad = Math.PI / 180;
        var dLat = (lat2 - lat1) * toRad;
        var dLng = (lng2 - lng1) * toRad;
        var a = Math.sin(dLat / 2) * Math.sin(dLat / 2)
            + Math.cos(lat1 * toRad) * Math.cos(lat2 * toRad)
            * Math.sin(dLng / 2) * Math.sin(dLng / 2);
        return 2 * 3958.8 * Math.asin(Math.min(1, Math.sqrt(a)));
    }

    function boundsForMarkers(markers) {
        var pts = (markers || []).filter(function (m) {
            return m.fitBounds
                && Number.isFinite(m.latitude)
                && Number.isFinite(m.longitude);
        });
        if (!pts.length)
            return null;

        var centerLat = median(pts.map(function (p) { return p.latitude; }));
        var centerLng = median(pts.map(function (p) { return p.longitude; }));
        var ranked = pts.map(function (p) {
            return {
                point: p,
                miles: haversineMiles(centerLat, centerLng, p.latitude, p.longitude)
            };
        }).sort(function (a, b) { return a.miles - b.miles; });

        var medianMiles = ranked[Math.floor(ranked.length / 2)].miles;
        var p85 = ranked[Math.min(ranked.length - 1, Math.floor(ranked.length * 0.85))].miles;
        var cutoff = Math.min(400, Math.max(5, p85 * 1.12, medianMiles * 2.5));
        var keep = ranked.filter(function (x) { return x.miles <= cutoff; });
        if (keep.length < Math.max(1, Math.ceil(pts.length * 0.7)))
            keep = ranked.slice(0, Math.max(1, Math.ceil(ranked.length * 0.85)));

        var bounds = L.latLngBounds([]);
        keep.forEach(function (x) {
            bounds.extend([x.point.latitude, x.point.longitude]);
        });
        return bounds.isValid() ? bounds : null;
    }

    function sizeMapElement(mapId) {
        var el = document.getElementById(mapId);
        if (!el)
            return el;
        var height = Math.max(580, Math.min(820, Math.round(window.innerHeight * 0.68)));
        el.style.height = height + 'px';
        return el;
    }

    function updateStatus(statusEl, routeCount, prospectCount, showProspects) {
        if (!statusEl)
            return;
        var text = 'Showing ' + routeCount + ' stop' + (routeCount === 1 ? '' : 's');
        if (showProspects && prospectCount > 0)
            text += ' and ' + prospectCount + ' prospect hotel' + (prospectCount === 1 ? '' : 's');
        statusEl.textContent = text;
    }

    function plot(options) {
        var markers = options.markers || [];
        var showProspects = !!options.showProspects;
        var statusEl = document.getElementById(options.statusId);
        var toggleEl = options.toggleId ? document.getElementById(options.toggleId) : null;
        var hintEl = options.hintId ? document.getElementById(options.hintId) : null;
        sizeMapElement(options.mapId);
        var map = L.map(options.mapId, {
            zoomSnap: 0,
            zoomDelta: 0.5
        });
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        }).addTo(map);

        var routeMarkers = [];
        var prospectMarkers = [];
        var routeLayer = L.layerGroup();
        var prospectLayer = L.layerGroup();

        markers.forEach(function (marker) {
            var latLng = L.latLng(marker.latitude, marker.longitude);
            var leafletMarker = L.marker(latLng, {
                icon: pinIcon(marker.color, marker.kind),
                zIndexOffset: marker.kind === 'prospect' ? 400 : 250,
                title: marker.name
            }).bindPopup(popupHtml(marker));

            if (marker.kind === 'prospect') {
                prospectMarkers.push(marker);
                prospectLayer.addLayer(leafletMarker);
            } else {
                routeMarkers.push(marker);
                routeLayer.addLayer(leafletMarker);
            }
        });

        routeLayer.addTo(map);
        if (showProspects && prospectMarkers.length)
            prospectLayer.addTo(map);

        var fitOptions = {
            padding: [8, 8],
            maxZoom: 16,
            animate: false
        };

        function visibleMarkers() {
            return showProspects ? routeMarkers.concat(prospectMarkers) : routeMarkers;
        }

        function applyFit() {
            var bounds = boundsForMarkers(visibleMarkers());
            if (bounds)
                map.fitBounds(bounds, fitOptions);
            else
                map.setView([38.5, -84.5], 7);
        }

        function setProspectsVisible(visible) {
            showProspects = !!visible;
            if (showProspects && prospectMarkers.length)
                map.addLayer(prospectLayer);
            else
                map.removeLayer(prospectLayer);

            if (hintEl)
                hintEl.classList.toggle('d-none', !showProspects || !prospectMarkers.length);

            updateStatus(statusEl, routeMarkers.length, prospectMarkers.length, showProspects);
        }

        applyFit();
        setProspectsVisible(showProspects);

        if (toggleEl) {
            toggleEl.checked = showProspects;
            toggleEl.addEventListener('change', function () {
                setProspectsVisible(toggleEl.checked);
            });
        }

        map.whenReady(function () {
            map.invalidateSize();
            applyFit();
        });
        setTimeout(function () {
            map.invalidateSize();
            applyFit();
        }, 250);
        return map;
    }

    return { plot: plot };
})();
