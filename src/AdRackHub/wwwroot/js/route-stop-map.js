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

    async function plot(options) {
        const stops = options.stops || [];
        const statusEl = document.getElementById(options.statusId);
        const map = L.map(options.mapId);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
        }).addTo(map);

        const bounds = L.latLngBounds([]);
        let plotted = 0;
        let failed = 0;

        for (let i = 0; i < stops.length; i++) {
            const stop = stops[i];
            if (statusEl)
                statusEl.textContent = `Plotting stops… ${i + 1} of ${stops.length}`;

            try {
                let latLng = null;
                if (stop.latitude != null && stop.longitude != null) {
                    latLng = { lat: stop.latitude, lng: stop.longitude };
                } else {
                    latLng = await geocode(stop.query);
                    if (i < stops.length - 1)
                        await delay(1100);
                }

                if (latLng) {
                    addMarker(map, bounds, stop, [latLng.lat, latLng.lng]);
                    plotted++;
                } else {
                    failed++;
                }
            } catch {
                failed++;
            }
        }

        if (plotted > 0) {
            map.fitBounds(bounds.pad(0.12));
            if (statusEl) {
                statusEl.textContent = `Showing ${plotted} of ${stops.length} stops` +
                    (failed > 0 ? ` (${failed} could not be located)` : '');
            }
        } else {
            if (statusEl)
                statusEl.textContent = 'Could not locate any stops on the map. Check stop addresses and try again.';
            map.setView([39.5, -84.5], 7);
        }

        setTimeout(function () { map.invalidateSize(); }, 200);
    }

    return { plot: plot };
})();
