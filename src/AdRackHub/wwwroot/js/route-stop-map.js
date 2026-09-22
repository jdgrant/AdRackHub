window.AdRackRouteMap = (function () {
    function delay(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    async function geocode(query) {
        const url = new URL('https://nominatim.openstreetmap.org/search');
        url.searchParams.set('format', 'json');
        url.searchParams.set('limit', '1');
        url.searchParams.set('countrycodes', 'us');
        url.searchParams.set('q', query);

        const response = await fetch(url.toString(), {
            headers: { 'Accept': 'application/json' }
        });
        if (!response.ok)
            return null;

        const results = await response.json();
        if (!results.length)
            return null;

        return {
            lat: parseFloat(results[0].lat),
            lng: parseFloat(results[0].lon)
        };
    }

    function addMarker(map, bounds, stop, latLng) {
        const label = stop.stepNumber != null ? `#${stop.stepNumber} ${stop.name}` : stop.name;
        const address = stop.address && stop.address !== '—' ? stop.address : stop.query;
        L.marker(latLng).addTo(map).bindPopup(`<strong>${label}</strong><br>${address}`);
        bounds.extend(latLng);
    }

    function hasCoords(stop) {
        return stop.latitude != null && stop.longitude != null
            && Number.isFinite(stop.latitude) && Number.isFinite(stop.longitude);
    }

    async function plot(options) {
        const stops = options.stops || [];
        const statusEl = document.getElementById(options.statusId);
        const mapEl = document.getElementById(options.mapId);
        if (!mapEl || typeof L === 'undefined')
            return;

        const map = L.map(options.mapId);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        }).addTo(map);

        const bounds = L.latLngBounds([]);
        let plotted = 0;
        let failed = 0;
        const missing = [];

        for (let i = 0; i < stops.length; i++) {
            const stop = stops[i];
            if (hasCoords(stop)) {
                addMarker(map, bounds, stop, [stop.latitude, stop.longitude]);
                plotted++;
            } else {
                missing.push(stop);
            }
        }

        if (plotted > 0)
            map.fitBounds(bounds.pad(0.12), { animate: false, maxZoom: 16 });
        else
            map.setView([38.5, -84.5], 7);

        if (missing.length === 0) {
            if (statusEl)
                statusEl.textContent = `Showing ${plotted} stop` + (plotted === 1 ? '' : 's');
            setTimeout(function () { map.invalidateSize(); }, 200);
            return;
        }

        for (let i = 0; i < missing.length; i++) {
            const stop = missing[i];
            if (statusEl)
                statusEl.textContent = `Locating remaining stops… ${i + 1} of ${missing.length}`;

            try {
                const latLng = stop.query ? await geocode(stop.query) : null;
                if (latLng) {
                    addMarker(map, bounds, stop, [latLng.lat, latLng.lng]);
                    plotted++;
                    map.fitBounds(bounds.pad(0.12), { animate: false, maxZoom: 16 });
                } else {
                    failed++;
                }
            } catch {
                failed++;
            }

            if (i < missing.length - 1)
                await delay(1100);
        }

        if (plotted > 0) {
            map.fitBounds(bounds.pad(0.12), { animate: false, maxZoom: 16 });
            if (statusEl) {
                statusEl.textContent = `Showing ${plotted} of ${stops.length} stops` +
                    (failed > 0 ? ` (${failed} could not be located)` : '');
            }
        } else if (statusEl) {
            statusEl.textContent = 'Could not locate any stops on the map. Check stop addresses and try again.';
        }

        setTimeout(function () { map.invalidateSize(); }, 200);
    }

    return { plot: plot };
})();
