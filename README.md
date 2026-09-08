# Jellyfin Terminal Plugin

[![Build and Verify](https://github.com/emulemexico/Jellyfin-Terminal/actions/workflows/build.yml/badge.svg)](https://github.com/emulemexico/Jellyfin-Terminal/actions/workflows/build.yml)
[![Validate Release](https://github.com/emulemexico/Jellyfin-Terminal/actions/workflows/release.yml/badge.svg)](https://github.com/emulemexico/Jellyfin-Terminal/actions/workflows/release.yml)
[![Target Jellyfin](https://img.shields.io/badge/Jellyfin-10.11%2B%20%2F%2010.10%2B-purple.svg)](https://jellyfin.org)
[![Target Framework](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com)

**Jellyfin Terminal** es un plugin para Jellyfin que integra una terminal web interactiva en tiempo real directamente en el Dashboard de administración de tu servidor Jellyfin, permitiéndote ejecutar comandos del sistema operativo anfitrión sin necesidad de abrir clientes SSH o consolas externas.

---

## ⚡ Características

- **Terminal Web Completa**: Renderizada con [xterm.js](https://xtermjs.org/) y `xterm-addon-fit`, con soporte de secuencias ANSI, colores, combinaciones de teclas e historial interactivo.
- **Comunicación en Tiempo Real vía WebSockets**: Conexión bidireccional asíncrona no bloqueante entre el navegador y el proceso shell.
- **Multiplataforma Nativa**:
  - **Linux / Docker / macOS**: Inicia automáticamente `/bin/bash` o `/bin/sh` en modo interactivo.
  - **Windows**: Inicia `PowerShell.exe` o `cmd.exe`.
- **Seguridad Estricta de Administrador**: Endpoint protegido mediante `[Authorize(Policy = Policies.RequiresElevation)]`. Únicamente los usuarios con permisos de administrador en Jellyfin pueden acceder al endpoint y abrir una sesión.
- **Ciclo de Vida Limpio**: Los procesos de terminal se cierran y terminan de forma segura al cerrar la pestaña o desconectarse el WebSocket, liberando hilos y memoria.

---

## 🚀 Instalación en Jellyfin

### Método 1: Catálogo mediante Repositorio de Plugins (Recomendado)

1. Abre tu **Jellyfin** con un usuario Administrador y ve al **Dashboard** (Consola de administración).
2. Ve a **Plugins** > pestaña **Repositorios** y haz clic en el botón **`+`** para añadir un nuevo repositorio.
3. Rellena los datos:
   - **Nombre del repositorio**: `Jellyfin Terminal`
   - **URL del repositorio**:
     ```text
     https://raw.githubusercontent.com/emulemexico/Jellyfin-Terminal/main/manifest.json
     ```
4. Guarda los cambios y ve a la pestaña **Catálogo**.
5. Busca el plugin **Terminal** e instálalo.
6. **Reinicia** el servidor Jellyfin.
7. Una vez reiniciado, verás la pestaña **Terminal** en el menú lateral izquierdo bajo la sección de administración del servidor.

---

### Método 2: Instalación Manual

1. Descarga el archivo `Jellyfin.Plugin.Terminal_<version>.zip` desde la sección [Releases](https://github.com/emulemexico/Jellyfin-Terminal/releases).
2. Descomprime la carpeta en el directorio de plugins de tu servidor Jellyfin:
   - **Linux / Docker**: `/config/plugins/Terminal/`
   - **Windows**: `C:\ProgramData\Jellyfin\Server\plugins\Terminal\`
3. Reinicia Jellyfin.

---

## 🛠️ Desarrollo y Compilación Local

### Requisitos
- SDK de .NET 9.0 o superior
- Git y GitHub CLI (`gh`) para releases

### Compilar
```bash
dotnet restore
dotnet build -c Release
```

### Publicar una nueva versión con Release automático
```powershell
.\scripts\release.ps1 -Version 1.0.0.0 -Changelog "Primera versión inicial de Terminal para Jellyfin"
```

---

## 🔒 Consideraciones de Seguridad

> [!CAUTION]
> Este plugin proporciona acceso de ejecución de comandos interactivo en el sistema operativo del anfitrión donde se ejecuta Jellyfin. Mantén restringidas tus cuentas de administrador con contraseñas seguras y autenticación de dos factores.