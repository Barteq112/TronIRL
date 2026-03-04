# Tron Reality - Location-Based Multiplayer Game (Prototype)

Tron is a Location-Based Multiplayer game prototype. The application brings the mechanics known from the cult movie "TRON"—building impassable light walls behind a moving vehicle—into the real world by utilizing precise smartphone GPS locations.

This project features real-time communication and consists of two main software artifacts: a central server and a mobile client.

##  Tech Stack

The architecture is entirely based on the .NET 8 ecosystem:
 
**Frameworks:** .NET 8, MAUI, ASP.NET Core Blazor Server 


**Communication:** gRPC, UDP Broadcast 



##  System Architecture

### 1. Server and Admin Panel (Web)

Built with ASP.NET Core, the server acts as the game arbiter and provides an administrative interface.

**Blazor Server:** Handles the presentation layer, allowing administrators to draw and manage game arenas directly on a map.


**GameManager:** The core game engine running as a Singleton. It ensures a consistent game state for all clients and uses concurrent collections (`ConcurrentDictionary`) to manage active sessions and player positions safely. It also verifies if players remain inside the defined polygon arenas.


**UDP Discovery Service:** A background service (`IHostedService`) that continuously listens on a specific UDP port. It allows mobile clients to broadcast a search request and automatically discover the server on the local network, eliminating manual IP configuration.



### 2. Mobile Client (MAUI)

The mobile client transforms a smartphone into a game controller that transmits coordinates and visualizes the gameplay.


**Hybrid Architecture:** Built with .NET MAUI, it combines native execution with web-based UI.


**UI Layer:** The interface is written in HTML/CSS and rendered via `Blazor WebView`, allowing the reuse of web components.


**Native Layer:** ViewModels have direct access to native device hardware (GPS, Network) via Android/iOS APIs, bypassing the JavaScript bridge for better performance.



## Communication & Mechanics

### gRPC Protocol

Data exchange relies heavily on gRPC and Protobuf for high binary performance.


**Server Streaming:** The client sends a request once, and the server pushes game state updates in a loop.

 
**Event-Driven Updates:** The server sends updates to the client only when a change occurs, significantly minimizing bandwidth usage.


**Client Updates:** The mobile app continuously sends its GPS position updates to the server.



### Algorithmic Collision Detection

The system operates on raw geographic coordinates (Latitude/Longitude) and features two main collision detection phases:

**Geofencing:** To verify if a player is within the arena, the system casts a ray from the player's position. If the number of intersections with the arena's polygon edges is odd, the player is inside.


**Segment Intersection (Light Walls):** The player's trail is a list of line segments. For every new movement segment, the system iterates through all existing segments of all players. If any segments intersect, a collision is registered, and the player is eliminated.


**Anti-Jitter Filtering:** A new point is added to a player's trail only if the distance from the last point exceeds a set threshold (e.g., 1.5 meters) to filter out GPS noise.



##  Authors



* Bartosz Sędzikowski 