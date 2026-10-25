# Cliente de compilación

`CompilationClient` toma la carpeta del archivo principal como raíz del proyecto y envía los recursos al adaptador HTTP local. Los textos utilizan `content`; las imágenes, PDFs incluidos y fuentes utilizan `base64`, conservando sus bytes y rutas relativas.

Se excluyen enlaces, directorios de Git/dependencias/compilación y extensiones no admitidas. Los límites son 20 MB de recursos, 5 MB de texto, 2000 archivos y 32 MB de JSON codificado. El adaptador valida los mismos límites y transforma los binarios en URLs temporales accesibles para CLSI.

El PDF y el log se descargan a una caché separada por proyecto y compilación. La app conserva el PDF anterior si LaTeX falla. Las pruebas repetibles están en `tests/BinaryCompilation.Integration`.
