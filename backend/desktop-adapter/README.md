# Adaptador local para Overleaf CLSI

Requiere .NET 10, Docker Desktop y el contenedor `sharelatex` funcionando. Desde la raíz de este repositorio:

```powershell
dotnet run --project backend/desktop-adapter
```

Escucha únicamente en `http://127.0.0.1:5017`. En otra terminal:

```powershell
dotnet run --project src/LatexDesktopCustom -- --compile .\plantilla_apa\main.tex
```

También puedes abrir la app normalmente: detecta `plantilla_apa/main.tex` en el repositorio. Pulsa **Compilar**, o **Seleccionar .tex** para cambiar el archivo principal.

## Conexión con Overleaf

El adaptador recibe HTTP desde C# y ejecuta `docker exec -i sharelatex curl` para llegar al CLSI interno en `127.0.0.1:3013`. El JSON se transmite por stdin UTF-8, sin shell. Así no hace falta publicar CLSI ni modificar el Compose existente. Las salidas se descargan por el puerto 80 ya publicado por Overleaf.

Configuración opcional mediante variables de entorno:

- `Overleaf__Container`: nombre del contenedor (por defecto `sharelatex`).
- `Overleaf__Url`: dirección HTTP de Overleaf (por defecto `http://127.0.0.1`).

## API

- `GET /health`: disponibilidad del adaptador (no verifica Docker).
- `POST /api/projects/{id}/compile`: compila recursos de texto y binarios. El ID contiene 24 caracteres hexadecimales.
- `GET /api/projects/{id}/builds/{buildId}/files/output.pdf`: entrega el PDF.
- La misma ruta con `output.log` o `output.synctex.gz` entrega esas salidas.
- `POST /api/projects/{id}/builds/{buildId}/synctex`: recibe `{ "page": 1, "h": 150, "v": 300 }` y devuelve `{ "code": [{ "file": "capitulos/introduccion.tex", "line": 10, "column": 1 }] }`. Página, línea y columna comienzan en 1; las coordenadas están en puntos PDF desde la esquina superior izquierda.

```json
{
  "rootResourcePath": "main.tex",
  "compiler": "pdflatex",
  "resources": [
    { "path": "main.tex", "content": "\\documentclass{article}\\begin{document}Hola\\end{document}" },
    { "path": "imagenes/figura.png", "base64": "...bytes del archivo codificados en Base64..." }
  ]
}
```

Devuelve `status`, `buildId`, `pdfUrl`, `logUrl` y `error`. HTTP 200 no garantiza que LaTeX haya compilado: revisar `status`. Las rutas de salida son relativas a este adaptador.

## Alcance de esta etapa

Se envían archivos de texto (incluidos `.tex`, `.bib`, `.sty` y `.cls`) y recursos binarios conservando subcarpetas. Cada recurso debe tener exactamente uno de los campos `content` o `base64`. El directorio del archivo principal es la raíz del proyecto. No se publica el proyecto en la lista de proyectos del editor web.

Los binarios se ofrecen a CLSI mediante un servidor HTTP temporal de Node dentro del contenedor existente. Escucha únicamente en su loopback, usa un token aleatorio por compilación y entrega solo los archivos de esa petición. Se cierra al terminar, fallar o cancelar; tiene además un plazo de seguridad de 120 segundos. No se publican puertos adicionales, no se escribe la imagen en el repositorio de Overleaf y no se cambia el Compose.

El cliente incluye PNG, JPEG, PDF, EPS, PS, SVG, WebP, GIF, BMP, TIFF y fuentes OTF/TTF/WOFF/WOFF2. Poder transmitir un formato no significa que pdfLaTeX pueda dibujarlo: PNG, JPEG y PDF están probados; otros formatos pueden necesitar conversión o un motor/paquete específico.

Límites: 2000 archivos, 20 MB de recursos sin codificar, 5 MB de texto y 32 MB de petición JSON. No se recorren enlaces simbólicos ni directorios de Git, dependencias o compilación.

La app guarda cada resultado en `%LOCALAPPDATA%\LatexDesktopCustom\Builds`, actualiza WebView2 al obtener un PDF y conserva el PDF anterior si falla. **Ver log** abre el diagnóstico recibido del compilador.

## Prueba de integración

Con Docker, `sharelatex` y este adaptador funcionando, desde la raíz:

```powershell
dotnet run --project tests/BinaryCompilation.Integration
```

Prueba el cliente C# real con texto, PNG, JPEG y un PDF incluido; nombres con espacios y acentos; cambio de imagen con el mismo nombre; y rechazo de Base64 y rutas inválidos. Utiliza `pdfimages` dentro de `sharelatex` para verificar las dimensiones de las imágenes incrustadas, sin instalar herramientas en Windows. Conserva los archivos de prueba en `artifacts/binary-integration` y la caché de PDFs de la app.

Los paquetes de TeX Live necesarios siguen dependiendo del documento. No se instalan automáticamente ni se modifica la plantilla.

## SyncTeX por versión del PDF

El adaptador ejecuta el `synctex` instalado en Overleaf sobre `generated-files/{buildId}/output.pdf` y su `.synctex.gz`. Se usa explícitamente la caché de ese build: en la versión de CLSI revisada, la ruta nativa `/sync/pdf?buildId=...` puede ejecutar la consulta contra la ruta absoluta de la compilación más reciente. Las pruebas verifican que un PDF anterior siga devolviendo sus líneas originales después de otra compilación.

Las rutas de salida se normalizan con relación al directorio de fuentes del proyecto y se rechazan rutas externas. Si el build ha caducado, se devuelve HTTP 404 para que la app solicite recompilar. No se edita el código ni la configuración de Overleaf.
