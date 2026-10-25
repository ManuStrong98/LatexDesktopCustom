# Integración con Neovim

La app inicia Neovim con `--listen` en una tubería con nombre exclusiva de cada ventana. No se modifica la configuración personal de Neovim.

**Seleccionar .tex** abre el archivo en la sesión del panel izquierdo y cambia el directorio de Neovim a la carpeta de ese archivo. La selección usada para compilar solo cambia si el editor confirma la apertura. Si el buffer actual tiene cambios sin guardar, la operación se rechaza con un mensaje y se conserva el archivo anterior.

Al iniciar, el proyecto seleccionado se pasa a Neovim como archivo inicial y Windows PowerShell arranca en su directorio. Si existe `plantilla_apa/main.tex` en el repositorio, se utiliza como selección inicial; `--compile <archivo.tex>` tiene prioridad.

Las llamadas posteriores utilizan el cliente de Neovim en modo `--headless --clean --remote-expr`: no abren otra terminal ni cargan otra copia de la configuración del editor. Las rutas se transmiten como JSON y se escapan al ejecutar comandos de Neovim.

La navegación por línea mediante SyncTeX todavía no está implementada.
