// wwwroot/js/mapLogic.js

let map;
let rectangle;
let dotNetHelper;
let isProgrammaticUpdate = false;

// Zmienne dla podglądu (ManageGames)
let viewMap;
let viewRectangle;
const mapObjects = {}; // Przechowuje markery i linie graczy

// --- 1. KREATOR (AddGame) ---
export function initMap(dotNetRef, startLat, startLon) {
    dotNetHelper = dotNetRef;
    const center = { lat: startLat, lng: startLon };

    map = new google.maps.Map(document.getElementById("map"), {
        zoom: 15,
        center: center,
        mapTypeId: "roadmap",
        streetViewControl: false,
        clickableIcons: false
    });

    const bounds = {
        north: startLat + 0.0022,
        south: startLat - 0.0022,
        east: startLon + 0.0035,
        west: startLon - 0.0035
    };

    rectangle = new google.maps.Rectangle({
        bounds: bounds,
        editable: true,
        draggable: true,
        strokeColor: "#FF0000",
        strokeOpacity: 0.8,
        strokeWeight: 2,
        fillColor: "#FF0000",
        fillOpacity: 0.35,
        map: map
    });

    rectangle.addListener("bounds_changed", () => {
        if (isProgrammaticUpdate) return;
        updateBlazor();
    });

    updateBlazor();
}

function updateBlazor() {
    const bounds = rectangle.getBounds();
    const ne = bounds.getNorthEast();
    const sw = bounds.getSouthWest();
    dotNetHelper.invokeMethodAsync("UpdateCoordinates", sw.lat(), ne.lat(), sw.lng(), ne.lng());
}

export function updateRectangle(lat, lon, widthMeters, heightMeters) {
    if (!map || !rectangle) return;
    isProgrammaticUpdate = true;

    const newCenter = { lat: lat, lng: lon };
    map.panTo(newCenter);

    // Prosta konwersja metrów na stopnie
    const latOffset = (heightMeters / 2.0) / 111132.0;
    const metersPerDegLon = 40075000.0 * Math.cos(lat * Math.PI / 180.0) / 360.0;
    const lonOffset = (widthMeters / 2.0) / metersPerDegLon;

    const newBounds = {
        north: lat + latOffset,
        south: lat - latOffset,
        east: lon + lonOffset,
        west: lon - lonOffset
    };

    rectangle.setBounds(newBounds);
    setTimeout(() => { isProgrammaticUpdate = false; }, 100);
}

// --- 2. PODGLĄD (ManageGames) ---
export function viewGameOnMap(minLat, maxLat, minLon, maxLon) {
    const mapDiv = document.getElementById("viewMap");
    if (!mapDiv) return;

    const center = { lat: (minLat + maxLat) / 2.0, lng: (minLon + maxLon) / 2.0 };

    if (!viewMap) {
        viewMap = new google.maps.Map(mapDiv, {
            zoom: 16,
            center: center,
            mapTypeId: "roadmap", // Możesz zmienić na 'satellite' dla lepszego efektu
            streetViewControl: false,
            mapTypeControl: false,
        });
    } else {
        viewMap.setCenter(center);
        viewMap.setZoom(16);
    }

    const bounds = { north: maxLat, south: minLat, east: maxLon, west: minLon };

    if (viewRectangle) viewRectangle.setMap(null);

    // RYSOWANIE ŚCIANY GRANICZNEJ
    viewRectangle = new google.maps.Rectangle({
        bounds: bounds,
        editable: false,
        draggable: false,
        strokeColor: "#1c0800", // Biała "elektryczna" ściana
        strokeOpacity: 1.0,
        strokeWeight: 4,      // Gruba linia
        fillColor: "#000000",
        fillOpacity: 0.1,     // Lekko przyciemniony środek
        map: viewMap,
        clickable: false
    });
}

// --- 3. LIVE UPDATE (WIZUALIZACJA TRON) ---
export function updateGameView(gameData) {
    if (!viewMap) return;

    // Jeśli gra została zatrzymana, czyścimy linie
    if (!gameData.isRunning) {
        clearTrails();
    }

    const players = gameData.players;
    if (!players) return;

    for (const key in players) {
        if (players.hasOwnProperty(key)) {
            updateSinglePlayer(players[key]);
        }
    }

    cleanupLeftPlayers(players);
}

function updateSinglePlayer(player) {
    const name = player.name;

    // Sprawdzamy, czy gracz jest "martwy" (czarny kolor z serwera)
    const isDead = player.color === "#000000";

    if (!mapObjects[name]) {
        // --- TWORZENIE NOWEGO GRACZA ---
        const marker = new google.maps.Marker({
            position: { lat: player.latitude, lng: player.longitude },
            map: viewMap,
            title: name,
            zIndex: 100, // Marker zawsze nad linią
            label: {
                text: name, // PEŁNA NAZWA
                color: "white",
                fontWeight: "bold",
                fontSize: "12px",
                className: "player-label" // Opcjonalnie do CSS
            },
            icon: {
                path: google.maps.SymbolPath.CIRCLE,
                scale: 7,
                fillColor: player.color,
                fillOpacity: 1,
                strokeWeight: 2,
                strokeColor: "black"
            }
        });

        const polyline = new google.maps.Polyline({
            path: [],
            geodesic: true,
            strokeColor: player.color,
            strokeOpacity: 0.8,
            strokeWeight: 4,
            map: viewMap,
            zIndex: 1 // Linia pod markerem
        });

        mapObjects[name] = { marker: marker, polyline: polyline };
    }

    // --- AKTUALIZACJA ---
    const obj = mapObjects[name];

    // 1. Aktualizacja pozycji markera
    obj.marker.setPosition({ lat: player.latitude, lng: player.longitude });

    // 2. Aktualizacja wyglądu (jeśli np. gracz umarł i zmienił kolor na czarny)
    const icon = obj.marker.getIcon();
    if (icon.fillColor !== player.color) {
        icon.fillColor = player.color;
        obj.marker.setIcon(icon);
        // Jeśli umarł, zmieniamy też kolor linii
        obj.polyline.setOptions({ strokeColor: player.color });
    }

    // 3. Aktualizacja ścieżki (linii)
    if (player.trail && player.trail.length > 0) {
        // Mapujemy współrzędne z C# na Google Maps
        // Używamy 'lat' i 'lon' (małe litery), bo tak domyślnie serializuje JSON
        obj.polyline.setPath(player.trail.map(t => ({ lat: t.lat, lng: t.lon })));
    } else {
        obj.polyline.setPath([]);
    }
}

function clearTrails() {
    for (const key in mapObjects) {
        mapObjects[key].polyline.setPath([]);
    }
}

function cleanupLeftPlayers(currentPlayers) {
    for (const key in mapObjects) {
        if (!currentPlayers.hasOwnProperty(key)) {
            mapObjects[key].marker.setMap(null);
            mapObjects[key].polyline.setMap(null);
            delete mapObjects[key];
        }
    }
}