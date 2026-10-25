# Visor PDF.js en WebView2

El visor usa PDF.js 6.4.299, incluido localmente en `vendor/pdfjs` con sus licencias. No necesita internet ni npm al ejecutar o compilar la app. Los archivos se copian a la salida y a la publicación mediante el `.csproj`.

`index.html` y `viewer.mjs` proporcionan páginas, zoom, ajuste de ancho y texto seleccionable. C# entrega los bytes del PDF mediante un buffer compartido de WebView2 de solo lectura; JavaScript copia esos bytes antes de pasarlos al worker y libera el buffer compartido. No se expone la carpeta del documento al navegador ni se descargan recursos remotos para abrirlo.

## PDF → Neovim

1. Compila el proyecto desde la app.
2. Haz doble clic sobre texto o una figura del PDF.
3. El visor invierte la transformación de PDF.js para obtener página y coordenadas en puntos PDF, independientemente del zoom, la rotación o la escala CSS.
4. C# consulta SyncTeX para el identificador de compilación asociado al PDF visible.
5. La app valida la ruta, comprueba que los fuentes no cambiaron y abre el archivo/línea/columna aproximada en Neovim.

Los PDFs abiertos manualmente se pueden leer, pero no activan SyncTeX porque no tienen una compilación asociada. Si se modifica el código o hay cambios sin guardar en el editor, la app pide guardar/recompilar antes de navegar. La precisión de la columna depende de lo que proporcione SyncTeX; a menudo solo hay una línea y se usa la columna 1.

El documento lleva un identificador propio para descartar mensajes de PDFs anteriores. Los mensajes del navegador se aceptan únicamente desde el HTML local del visor. No se habilita el scripting de documentos PDF.

El control `PdfWebViewControl` captura el doble clic nativo de WPF y transmite su posición normalizada a PDF.js. Esto evita depender de la secuencia de clics que reenvía `WebView2CompositionControl`, afectada por el problema reportado en https://github.com/MicrosoftEdge/WebView2Feedback/issues/5099. Se mantiene el evento del navegador como alternativa y se deduplican ambas notificaciones para no ejecutar dos saltos.

## Pruebas

Con el adaptador y Overleaf funcionando, desde la raíz:

```powershell
node tests/pdf-coordinates.test.mjs
dotnet run --project tests/SyncTex.Integration
dotnet run --project tests/PdfViewer.Integration
```

La última prueba abre una ventana de WebView2 fuera de la pantalla, no mueve el mouse y verifica el doble clic de la capa de texto a distintos niveles de zoom hasta Neovim. Requiere Windows, WebView2 Runtime y Neovim. Las otras pruebas comprueban texto, figuras, archivos incluidos, versiones antiguas y nuevas, rutas con espacios/acentos y protección de cambios sin guardar.

También comprueba la función utilizada por el puente de WPF. Para una prueba física interactiva puede ejecutarse `dotnet run --project tests/PdfViewer.Integration -- --mouse` y hacer doble clic sobre «Texto para saltar» en la ventana de prueba.
