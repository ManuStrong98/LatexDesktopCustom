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
- `POST /api/projects/{id}/compile`: compila recursos de texto. El ID contiene 24 caracteres hexadecimales.
- `GET /api/projects/{id}/builds/{buildId}/files/output.pdf`: entrega el PDF.
- La misma ruta con `output.log` o `output.synctex.gz` entrega esas salidas.

```json
{
  "rootResourcePath": "main.tex",
  "compiler": "pdflatex",
  "resources": [
    { "path": "main.tex", "content": "\\documentclass{article}\\begin{document}Hola\\end{document}" }
  ]
}
```

Devuelve `status`, `buildId`, `pdfUrl`, `logUrl` y `error`. HTTP 200 no garantiza que LaTeX haya compilado: revisar `status`. Las rutas de salida son relativas a este adaptador.

## Alcance de esta etapa

Se envían archivos de texto (incluidos `.tex`, `.bib`, `.sty` y `.cls`) conservando subcarpetas. El directorio del archivo principal es la raíz del proyecto. La carga de imágenes y otros binarios queda para la siguiente etapa. No se publica el proyecto en la lista de proyectos del editor web.

La app guarda cada resultado en `%LOCALAPPDATA%\LatexDesktopCustom\Builds`, actualiza WebView2 al obtener un PDF y conserva el PDF anterior si falla. **Ver log** abre el diagnóstico recibido del compilador.

## Plantilla APA

En la prueba con el contenedor actual faltaron el idioma español de Babel y `newtxtext.sty`. Esto es una dependencia de TeX Live en el contenedor, no un fallo del adaptador. No se modificó la plantilla ni se instalaron paquetes en Overleaf automáticamente. Otros paquetes podrían faltar al resolver los primeros errores.
