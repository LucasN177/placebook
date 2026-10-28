// Kleine Browser-Helfer, die Blazor nicht direkt mitbringt.

export function downloadText(fileName, text, mimeType) {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType }));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}

const GEO_ERRORS = {
    1: 'Standortzugriff wurde verweigert.',
    2: 'Standort konnte nicht ermittelt werden.',
    3: 'Zeitüberschreitung bei der Standortbestimmung.',
};

export function getCurrentPosition() {
    return new Promise((resolve, reject) => {
        if (!navigator.geolocation) {
            reject(new Error('Dein Browser unterstützt keine Standortbestimmung.'));
            return;
        }
        navigator.geolocation.getCurrentPosition(
            p => resolve({ lat: p.coords.latitude, lon: p.coords.longitude, accuracy: p.coords.accuracy }),
            e => reject(new Error(GEO_ERRORS[e.code] ?? e.message)),
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
    });
}
