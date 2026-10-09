\<div align="center"\>

# 📝 LaTeX Desktop

### Tu terminal, tu editor y tu PDF en una sola ventana.

Una aplicación de escritorio para editar proyectos LaTeX con **PowerShell y Neovim**, controlar sus versiones con **Git** y compilar mediante el backend de **Overleaf Community Edition**.

Estado
Plataforma
Interfaz
Lenguaje
\</div\>

***

## 💡 Acerca del proyecto

LaTeX Desktop busca reunir la edición desde terminal y la visualización de documentos en una sola aplicación.

Los archivos del proyecto permanecen en una **carpeta local**, donde puedes utilizar Neovim, Git y tus herramientas habituales. El backend de Overleaf se encarga de generar el PDF.

La interfaz estará dividida en dos paneles ajustables:

| Panel izquierdo | Panel derecho |
|---|---|
| PowerShell con Neovim | Visor PDF con controles de compilación |
| Edición de archivos locales | Zoom y navegación entre páginas |
| Comandos de Git | Navegación del PDF al código con SyncTeX |

> 🚧 El proyecto está en desarrollo. Las funciones descritas a continuación son objetivos y todavía pueden no estar disponibles.

## ✨ Funciones previstas

- [ ] Terminal PowerShell integrada.
- [ ] Edición de proyectos mediante Neovim.
- [ ] Selección de una carpeta local de trabajo.
- [ ] Compilación desde un botón o mediante `Ctrl+S`.
- [ ] Actualización del PDF después de compilar.
- [ ] Controles de zoom y navegación entre páginas.
- [ ] Doble clic en el PDF para abrir el archivo y la línea correspondientes.
- [ ] Visualización de errores y logs de compilación.
- [ ] Conservación del último PDF válido cuando una compilación falle.
- [ ] Trabajo con repositorios Git locales.

## 🧰 Tecnologías

| Tecnología | Uso |
|---|---|
| **C\# y .NET** | Lógica de la aplicación |
| **WPF** | Ventana e interfaz de escritorio |
| **PowerShell** | Terminal de trabajo |
| **Neovim** | Edición del código LaTeX |
| **WebView2 y PDF.js** | Visualización del PDF |
| **Overleaf CLSI** | Servicio de compilación LaTeX |
| **SyncTeX** | Correspondencia entre PDF y código fuente |
| **Docker** | Ejecución del backend |
| **Git** | Control de versiones |

## 📋 Requisitos

Para ejecutar la aplicación base:

- Windows compatible con WPF.
- SDK de .NET compatible con la versión indicada en `LatexDesktop.csproj`.

Para las integraciones previstas también serán necesarios:

- PowerShell.
- Neovim.
- Git.
- Docker Desktop con contenedores Linux.
- Microsoft Edge WebView2 Runtime.
- Backend de compilación configurado con los paquetes LaTeX necesarios.

> **PowerShell:** `powershell.exe` corresponde a Windows PowerShell clásico; PowerShell 7 utiliza `pwsh.exe`.

## 🚀 Cómo ejecutar

Abre una terminal en la carpeta que contiene `LatexDesktop.csproj`.

### 1\. Restaurar dependencias

```
dotnet restore
```

### 2\. Iniciar la aplicación

```
dotnet run
```

En la base generada con `dotnet new wpf`, se abrirá una ventana vacía. Los paneles y las integraciones se añadirán durante el desarrollo.

### Compilar sin ejecutar

```
dotnet build
```

> La configuración y los comandos del backend se documentarán cuando se incorpore al proyecto.

## 📁 Estructura del proyecto

La base WPF contiene:

```
LatexDesktop/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── LatexDesktop.csproj
├── .gitignore
└── README.md
```

La organización prevista para las siguientes etapas es:

```
LatexDesktop/
├── Terminal/          # Integración de PowerShell
├── Editor/            # Comunicación con Neovim
├── Compilation/       # Coordinación de compilaciones
├── PdfViewer/         # Interfaz del visor PDF
├── backend/           # Adaptador para Overleaf CLSI
├── docker/            # Configuración del backend
└── nvim/              # Integración y atajos de Neovim
```

## ⚙️ Flujo de trabajo previsto

```
Abrir una carpeta LaTeX
          │
          ▼
Editar archivos con Neovim
          │
          ▼
Guardar y solicitar compilación con Ctrl+S
          │
          ▼
Enviar los archivos al backend
          │
          ▼
Compilar mediante Overleaf CLSI
          │
          ▼
Actualizar el visor PDF
```

El botón **Compilar** utilizará el mismo flujo que el atajo del editor.

Para navegar desde el PDF al código:

```
Doble clic en el PDF
          │
          ▼
Consultar página y coordenadas con SyncTeX
          │
          ▼
Obtener archivo, línea y columna
          │
          ▼
Abrir esa ubicación en Neovim
```

## 🌿 Control de versiones

La aplicación y los documentos LaTeX se gestionan en repositorios separados:

- **Repositorio de la aplicación:** código C\#, interfaz e integraciones.
- **Repositorio de cada documento:** archivos `.tex`, bibliografía, imágenes y demás recursos.

Desde la terminal del documento podrás utilizar tus comandos habituales:

```
git status
git add .
git commit -m "Actualizar documento"
```

## 🗺️ Etapas de desarrollo

1. Crear y ejecutar la base WPF.
2. Integrar PowerShell y comprobar el funcionamiento de Neovim.
3. Conectar la aplicación con el servicio de compilación.
4. Incorporar el visor PDF y sus controles.
5. Configurar la compilación desde Neovim.
6. Implementar la navegación mediante SyncTeX.
7. Mejorar el manejo de errores y la experiencia de uso.

***
\<div align="center"\>

**Edita desde la terminal. Versiona con Git. Visualiza tu LaTeX.**
\</div\>
