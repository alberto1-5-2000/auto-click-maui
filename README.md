# Auto Click MAUI + React (Windows)

Proyecto principal: `AutoClickMaui`.

La interfaz está hecha en React JS dentro de un `WebView` MAUI, y el backend nativo (C#) hace captura de pantalla + detección de imágenes + autoclick.

## Funciones

- Seleccionar monitor activo.
- Crear varias acciones (cada una con su imagen y punto de clic).
- Ejecutar en dos modos:
   - `Ruta ordenada`: se ejecutan en orden predefinido.
   - `Sin orden`: se detectan varias imágenes y se pulsa la que aparezca.
- Guardar y cargar perfiles completos.

## Estructura

- `AutoClickMaui/` proyecto MAUI.
- `AutoClickMaui/wwwroot/react-ui.html` interfaz React.
- `AutoClickMaui/Services/AutoClickEngine.cs` motor de detección/click.
- `AutoClickMaui/Services/ProfileStore.cs` persistencia de perfiles en JSON.

## Ejecutar

```powershell
dotnet run --project .\AutoClickMaui\AutoClickMaui.csproj
```

## Nota del entorno

Si el equipo no tiene workloads/SDK MAUI correctamente instalados para Windows, la compilación puede fallar antes de ejecutar la app. En ese caso, instala la carga MAUI de Visual Studio (.NET MAUI).
