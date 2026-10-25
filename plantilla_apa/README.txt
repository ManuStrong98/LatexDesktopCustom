PLANTILLA APA 7 MODULAR - pdfLaTeX
==================================

Archivo principal:
  main.tex

Estructura:
  main.tex                         -> coordina el documento
  configuracion.tex                -> paquetes, márgenes, fuente, APA, citas, figuras
  datos.tex                        -> título, autor, universidad, docente, fecha
  preliminares/portada.tex         -> portada
  preliminares/resumen.tex         -> resumen opcional
  capitulos/*.tex                  -> contenido por capítulos
  referencias.bib                  -> bibliografía
  imagenes/                        -> imágenes del documento

COMPILACIÓN
-----------
La plantilla usa pdfLaTeX y Biber:

  pdflatex main.tex
  biber main
  pdflatex main.tex
  pdflatex main.tex

En Overleaf:
- Compiler: pdfLaTeX
- LaTeX se encarga de ejecutar Biber automáticamente en compilaciones normales.

NOTA SOBRE LA FUENTE
--------------------
APA 7 admite Times New Roman 12 pt. Con pdfLaTeX no se usa la fuente del sistema
Times New Roman directamente; la plantilla usa newtxtext/newtxmath, de estilo Times,
compatible con pdfLaTeX.

NOTA SOBRE TÍTULOS
------------------
APA 7 puro no numera los encabezados, pero el ejemplo institucional proporcionado usa:
CAPÍTULO 2, 2.1, 2.5.1, etc. Por eso esta plantilla conserva esa numeración.
