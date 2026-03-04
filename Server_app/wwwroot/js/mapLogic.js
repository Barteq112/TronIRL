// wwwroot/js/mapLogic.js

// zmienne globalne
let map;
let rectangle;
let dotNetHelper;
let isProgrammaticUpdate = false;


let viewMap;
let viewRectangle;
const mapObjects = {}; 

// funkcje eksportowane do Blazor - dodaje mapę i prostokąt do edycji
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

// funkcja do aktualizacji współrzędnych w Blazor
function updateBlazor() {
    const bounds = rectangle.getBounds();
    const ne = bounds.getNorthEast();
    const sw = bounds.getSouthWest();
    dotNetHelper.invokeMethodAsync("UpdateCoordinates", sw.lat(), ne.lat(), sw.lng(), ne.lng());
}

// funkcja do aktualizacji prostokąta z Blazor
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

// funkcja do wyświetlania gry na mapie
export function viewGameOnMap(minLat, maxLat, minLon, maxLon) {
    const mapDiv = document.getElementById("viewMap");
    if (!mapDiv) return;

    const center = { lat: (minLat + maxLat) / 2.0, lng: (minLon + maxLon) / 2.0 };

    if (!viewMap) {
        viewMap = new google.maps.Map(mapDiv, {
            zoom: 16,
            center: center,
            mapTypeId: "roadmap", 
            streetViewControl: false,
            mapTypeControl: false,
        });
    } else {
        viewMap.setCenter(center);
        viewMap.setZoom(16);
    }

    const bounds = { north: maxLat, south: minLat, east: maxLon, west: minLon };

    if (viewRectangle) viewRectangle.setMap(null);

   
    viewRectangle = new google.maps.Rectangle({
        bounds: bounds,
        editable: false,
        draggable: false,
        strokeColor: "#1c0800", 
        strokeOpacity: 1.0,
        strokeWeight: 4,      
        fillColor: "#000000",
        fillOpacity: 0.1,     
        map: viewMap,
        clickable: false
    });
}

// funkcja do aktualizacji widoku gry - gracze i ich trasy
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

// funkcja do aktualizacji pojedynczego gracza
function updateSinglePlayer(player) {
    const name = player.name;


    const isDead = player.color === "#000000";

    if (!mapObjects[name]) {

        const marker = new google.maps.Marker({
            position: { lat: player.latitude, lng: player.longitude },
            map: viewMap,
            title: name,
            zIndex: 100, 
            label: {
                text: name, 
                color: "white",
                fontWeight: "bold",
                fontSize: "12px",
                className: "player-label" 
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
            zIndex: 1 
        });

        mapObjects[name] = { marker: marker, polyline: polyline };
    }


    const obj = mapObjects[name];


    obj.marker.setPosition({ lat: player.latitude, lng: player.longitude });

  
    const icon = obj.marker.getIcon();
    if (icon.fillColor !== player.color) {
        icon.fillColor = player.color;
        obj.marker.setIcon(icon);
        // Jeśli umarł, zmieniamy też kolor linii
        obj.polyline.setOptions({ strokeColor: player.color });
    }


    if (player.trail && player.trail.length > 0) {

        obj.polyline.setPath(player.trail.map(t => ({ lat: t.lat, lng: t.lon })));
    } else {
        obj.polyline.setPath([]);
    }
}


// funkcja do czyszczenia tras wszystkich graczy
function clearTrails() {
    for (const key in mapObjects) {
        mapObjects[key].polyline.setPath([]);
    }
}
// funkcja do usuwania graczy, którzy opuścili grę
function cleanupLeftPlayers(currentPlayers) {
    for (const key in mapObjects) {
        if (!currentPlayers.hasOwnProperty(key)) {
            mapObjects[key].marker.setMap(null);
            mapObjects[key].polyline.setMap(null);
            delete mapObjects[key];
        }
    }
}