# Guía de Publicación Oficial: Emby.Plugin.IntroDbApp
**Autor:** Andy Ernesto Ciprian Ciprian  
**Versión Inicial:** v4.2.3 Universal  
**Fecha:** Octubre 2026  

Este documento reúne todas las instrucciones, plantillas y especificaciones técnicas para llevar el complemento desde tu repositorio local de Git hasta el **Catálogo Oficial de Emby** (*Plugin Catalog*), abarcando desde la fase de presentación comunitaria hasta la integración final en la tienda.

---

## Índice
1. [Paso 0: Vinculación y Subida a GitHub (Cuando estés autenticado)](#paso-0-vinculación-y-subida-a-github)
2. [Fase 2: Presentación en el Foro Oficial de Emby Community](#fase-2-presentación-en-el-foro-oficial-de-emby-community)
3. [Fase 3: Solicitud de Inclusión en la Tienda Oficial (Plugin Catalog)](#fase-3-solicitud-de-inclusión-en-la-tienda-oficial-plugin-catalog)
4. [Fase 4: Repositorio Comunitario Personalizado (Instalación con 1 Clic)](#fase-4-repositorio-comunitario-personalizado-instalación-con-1-clic)
5. [Flujo de Mantenimiento y Nuevas Actualizaciones](#flujo-de-mantenimiento-y-nuevas-actualizaciones)

---

## Paso 0: Vinculación y Subida a GitHub

El código ya está completamente preparado localmente, empaquetado y etiquetado con el tag `v4.2.3`. Cuando decidas iniciar y te autentiques con GitHub (mediante `gh auth login` o tu clave SSH), ejecuta los siguientes comandos en tu terminal:

```bash
# 1. Ingresar al directorio del proyecto
cd /home/servidor/introdb_plugin

# 2. Si creas el repositorio en GitHub (ejemplo: Emby.Plugin.IntroDbApp), agrega el origen remoto:
git remote add origin https://github.com/Andy-Ciprian/Emby.Plugin.IntroDbApp.git

# 3. Empujar la rama master y las etiquetas de versión al repositorio remoto
git push -u origin master --tags
```

> [!NOTE]
> En cuanto hagas `git push --tags`, el flujo de trabajo de **GitHub Actions** (`.github/workflows/build.yml`) se disparará automáticamente en los servidores de GitHub, compilará el código en un entorno limpio de Ubuntu con .NET 8, creará la Release `v4.2.3` y adjuntará el archivo `Emby.Plugin.IntroDbApp.zip` listo para descargar.

---

## Fase 2: Presentación en el Foro Oficial de Emby Community

Emby requiere que todo plugin pase primero por el foro de la comunidad para recibir retroalimentación técnica y validar su estabilidad.

### ¿Dónde publicar?
1. Entra a [Emby Community Forums](https://emby.media/community/).
2. Inicia sesión y ve a la sección:  
   **Emby Server > Plugins & Applications > Plugin Development**  
   *(Enlace directo: `https://emby.media/community/index.php?/forum/64-plugin-development/`)*.
3. Haz clic en **"Start new topic"**.

### Plantilla lista para copiar y pegar (en inglés):

> **Título del Tema:**  
> `[Plugin] IntroDB.app - Universal Intro, Outro & Post-Credits Skip Detection (Premiere & Non-Premiere)`

```markdown
Hello Emby Community,

I am excited to introduce **IntroDB.app for Emby Server**, an autonomous and hybrid plugin designed to provide accurate, crowd-sourced marker detection for TV shows and Movies across all playback environments.

### 🌟 Why this plugin?
Many users enjoy automated skip features, but not all setups have Emby Premiere or run heavy audio-fingerprinting tasks that consume high CPU on low-power servers (like Raspberry Pi, NAS, or VPS). 

**IntroDB.app** bridges this gap by querying the open-access API at [introdb.app](https://introdb.app) and directly injecting native chapter markers (`MarkerType.IntroStart`, `MarkerType.IntroEnd`, and `MarkerType.CreditsStart`) into Emby's database.

### 🚀 Key Features:
- **Universal Compatibility:** Works seamlessly on all official Emby apps (Web, Android, iOS, Apple TV, Smart TVs) and external players like **Infuse**, **Kodi** (EmbyCon), and VLC.
- **Premiere & Non-Premiere Support:** Native chapter markers allow any player to recognize intros and end credits.
- **Multi-Engine Modes:**
  1. *IntroDB First (Default):* Queries IntroDB.app, gracefully falling back to TheIntroDB or local chapters if absent.
  2. *TheIntroDB First (Hybrid):* Prioritizes TheIntroDB and uses IntroDB as a rescue layer.
  3. *Standalone:* Operates purely with IntroDB.app.
  4. *Overwrite:* Forces complete alignment with IntroDB data.
- **Real-Time Playback Tracking:** Measures forward jumps (>15s) and credits skips to track seconds saved per user profile.
- **Live Diagnostics & Tools:**
  - One-click title scanner by IMDb ID.
  - Internal Emby chapter inspector.
  - Live API inspector and playback skip simulator.
  - Community timestamp contribution tool with automated validation.
- **Theme Adaptability:** Full CSS variable integration adapting dynamically to Emby Light, Dark, and custom themes.

### 📦 Installation:
1. Download `Emby.Plugin.IntroDbApp.zip` from our GitHub Releases.
2. Extract `Emby.Plugin.IntroDbApp.dll` into your Emby `plugins` folder.
3. Restart Emby Server.
4. Access **IntroDB.app** under the *Advanced* plugins menu.

### 🔗 Source Code & Releases:
- **GitHub Repository:** https://github.com/Andy-Ciprian/Emby.Plugin.IntroDbApp
- **API Documentation:** https://introdb.app/docs/api

Feedback, testing on various client devices, and feature suggestions are warmly welcomed!
```

---

## Fase 3: Solicitud de Inclusión en la Tienda Oficial (Plugin Catalog)

Una vez que el hilo tenga tracción (generalmente tras 1 o 2 semanas de pruebas y comentarios positivos de otros usuarios):

### 1. Contacto con los Desarrolladores Core de Emby
En el mismo hilo del foro, publica una respuesta mencionando a los líderes de desarrollo:
> *"@Luke @ebr The plugin has proven stable across multiple Emby 4.8+ setups. We would love to have IntroDB.app added to the official Emby Plugin Catalog for one-click installation by our community users. Please let me know what details or manifest entries you require."*

### 2. Especificación del Paquete que Emby registrará
El equipo de Emby registrará tu plugin en su índice maestro (`EmbyPackages.json`). Los metadatos estándar son:

| Campo | Valor |
|---|---|
| **name** | `IntroDbApp` |
| **guid** | `f62e84d1-817a-4c48-8cf9-a6f951e73792` |
| **versionStr** | `4.2.3.0` |
| **classification** | `Release` |
| **category** | `Playback` (o `General`) |
| **description** | `Universal Intro and Credits skip detection powered by IntroDB.app.` |
| **targetSystem** | `Server` |
| **targetVersion** | `>= 4.8.0.0` |
| **owner** | `Andy Ernesto Ciprian` |
| **sourceUrl** | `https://github.com/Andy-Ciprian/Emby.Plugin.IntroDbApp/releases/download/v4.2.3/Emby.Plugin.IntroDbApp.zip` |

---

## Fase 4: Repositorio Comunitario Personalizado (Instalación con 1 Clic)

Si deseas que usuarios o amigos puedan instalar y actualizar el plugin directamente desde su panel de Emby **sin esperar la aprobación de la tienda oficial**:

1. En tu repositorio de GitHub, habilita **GitHub Pages** sobre la rama `gh-pages` o la carpeta `docs/`.
2. Publica un archivo `manifest.json` accesible públicamente (ejemplo: `https://andy-ciprian.github.io/Emby.Plugin.IntroDbApp/manifest.json`) con la siguiente estructura:

```json
[
  {
    "guid": "f62e84d1-817a-4c48-8cf9-a6f951e73792",
    "name": "IntroDB.app",
    "description": "Detección y salto universal de intros y créditos en Emby mediante IntroDB.app.",
    "overview": "Sincroniza marcas de intros y créditos para clientes oficiales, Infuse y Kodi con o sin Premiere.",
    "owner": "Andy Ernesto Ciprian",
    "category": "Playback",
    "versions": [
      {
        "version": "4.2.3.0",
        "changelog": "Versión 4.2.3: Limpieza automática de aportes comunitarios, validación de simulación estricta y tema adaptativo.",
        "targetFilename": "Emby.Plugin.IntroDbApp.dll",
        "sourceUrl": "https://github.com/Andy-Ciprian/Emby.Plugin.IntroDbApp/releases/download/v4.2.3/Emby.Plugin.IntroDbApp.zip",
        "timestamp": "2026-10-31T18:31:00Z"
      }
    ]
  }
]
```

3. El usuario simplemente añade tu URL en **Emby > Configuración > Plugins > Repositorios (+) > Agregar Repositorio**.
4. ¡Listo! Tu plugin aparecerá inmediatamente en su catálogo local de Emby para instalar y actualizar con 1 clic.

---

## Flujo de Mantenimiento y Nuevas Actualizaciones

Para futuras versiones (ejemplo: `v4.2.4`):
1. Modificas el código y actualizas la versión en `Emby.Plugin.IntroDbApp.csproj`.
2. Haces commit y creas el nuevo tag:
   ```bash
   git tag v4.2.4
   git push origin master --tags
   ```
3. GitHub Actions compilará automáticamente el zip y publicará la release.
4. Si estás en el catálogo oficial, notificas al hilo de Emby para que actualicen el número de versión; si usas el repositorio personalizado, solo añades el nuevo bloque de versión en tu `manifest.json`.
