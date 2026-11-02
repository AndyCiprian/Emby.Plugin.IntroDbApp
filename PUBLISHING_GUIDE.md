# Guía de Publicación Oficial: Emby.Plugin.IntroDbApp
**Autor:** Andy Ernesto Ciprian Ciprian  
**Versión:** v4.3.0 Universal Edition  
**Fecha:** Noviembre 2026  

Este documento reúne todas las instrucciones, plantillas y especificaciones técnicas para llevar el complemento desde tu repositorio de GitHub hasta la comunidad de Emby y el **Catálogo Oficial de Emby** (*Plugin Catalog*), permitiendo tanto la instalación comunitaria inmediata vía repositorio personalizado (Fase 4) como la presentación oficial y aprobación por el equipo de Emby (Fases 2 y 3).

---

## Índice
1. [Estado Actual: Código y Release Publicados](#estado-actual-código-y-release-publicados)
2. [Fase 2: Presentación en el Foro Oficial de Emby Community](#fase-2-presentación-en-el-foro-oficial-de-emby-community)
3. [Fase 3: Solicitud de Inclusión en la Tienda Oficial (Plugin Catalog)](#fase-3-solicitud-de-inclusión-en-la-tienda-oficial-plugin-catalog)
4. [Fase 4: Repositorio Comunitario Personalizado (Instalación con 1 Clic Inmediata)](#fase-4-repositorio-comunitario-personalizado-instalación-con-1-clic-inmediata)
5. [Flujo de Mantenimiento y Nuevas Actualizaciones](#flujo-de-mantenimiento-y-nuevas-actualizaciones)

---

## Estado Actual: Código y Release Publicados

- **Repositorio Oficial:** https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp
- **Última Release Estable:** [v4.3.0 Universal Edition](https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp/releases/tag/v4.3.0)
- **Artefactos Compilados y Adjuntos:**
  - `Emby.Plugin.IntroDbApp.dll` (288.50 KiB)
  - `Emby.Plugin.IntroDbApp.zip` (78.98 KiB)
- **CI/CD Pipeline:** Automatizado vía GitHub Actions en cada tag.

---

## Fase 2: Presentación en el Foro Oficial de Emby Community

Emby requiere que todo plugin pase primero por el foro de la comunidad para recibir retroalimentación técnica, validar su estabilidad en diferentes clientes (Web, Android, iOS, Apple TV, Smart TVs) y sumar usuarios interesados.

### ¿Dónde publicar?
1. Entra a [Emby Community Forums](https://emby.media/community/).
2. Inicia sesión con tu cuenta de Emby y dirígete a:  
   **Emby Server > Plugins & Applications > Plugin Development**  
   *(Enlace directo: `https://emby.media/community/index.php?/forum/64-plugin-development/`)*.
3. Haz clic en el botón **"Start new topic"**.

---

### Plantilla Lista para Copiar y Pegar (en inglés)

> **Título del Tema:**  
> `[Plugin] IntroDB.app - Universal Intro, Credits & Post-Credits Skip Detection (Visual Explorer, JSON Backup & 14-day TTL)`

```markdown
Hello Emby Community,

I am excited to introduce **IntroDB.app for Emby Server (v4.3.0 Universal Edition)**, an autonomous and hybrid plugin designed to provide accurate, crowd-sourced marker detection for TV shows and Movies across all playback environments.

### 🌟 Why this plugin?
Many users enjoy automated skip features, but not all setups have Emby Premiere or run heavy audio-fingerprinting tasks that consume high CPU on low-power servers (like Raspberry Pi, NAS, or VPS). 

**IntroDB.app** bridges this gap by querying the open-access API at [introdb.app](https://introdb.app) and directly injecting native chapter markers (`MarkerType.IntroStart`, `MarkerType.IntroEnd`, and `MarkerType.CreditsStart`) into Emby's database.

---

### 🚀 Key Features in v4.3.0:

1. **🎨 Visual Chapter Explorer & Inline Editor:**
   - Browse all series and episodes directly from your Emby Dashboard.
   - Inspect existing Intro/Credits/Post-Credits markers with high-contrast badges.
   - Edit and fine-tune chapter start/end timestamps on the fly without server restarts.

2. **💾 Universal JSON Backup & Restoration Engine:**
   - Export your entire library's chapter markers to structured, portable JSON files.
   - Restore chapter markers anytime using Emby's native interactive directory browser (`directorybrowser`) or manual OS paths.

3. **🚫 Auto-Skip Exclusions:**
   - Selectively exclude specific series (e.g. anime with favorite openings or endings) from automated skipping and telemetry.

4. **🔄 14-Day Progressive 404 Auto-Expiration (TTL):**
   - Missing items cached in the 404 index automatically expire after 14 days.
   - The scanner progressively re-evaluates expired items in safe batches of up to 30 per nightly scan, respecting API rate limits.
   - When community users contribute missing segments upstream, the plugin recovers them automatically and decreases the 404 cache count.

5. **📜 Disk-Persisted Activity Logger:**
   - Persistent audit log saved to `introdb_activity.json` that survives server reboots.
   - 200-item milestone checkpoints during nightly scans and full ingest tracking.

6. **⚡ Resilient Real-Time Ingestion:**
   - Event-driven detection on `ItemAdded` with multi-attempt delay logic ensuring metadata providers assign IMDb IDs before querying upstream.

7. **Universal Client Compatibility:**
   - Works seamlessly on all official Emby apps (Web, Android, iOS, Apple TV, Smart TVs) and external players like **Infuse**, **Kodi** (EmbyCon/Jellyfin sync), and VLC.
   - Works with or without Emby Premiere.

8. **Multi-Engine Modes:**
   - ⭐ *IntroDB First (Default):* Queries IntroDB.app, gracefully falling back to TheIntroDB or local chapters if absent.
   - 🔄 *TheIntroDB First (Hybrid):* Prioritizes TheIntroDB and uses IntroDB as a rescue layer.
   - 🛡️ *Standalone:* Operates purely with IntroDB.app.
   - ⚡ *Overwrite:* Forces complete alignment with IntroDB data.

---

### 📦 Installation:

#### Option A: 1-Click Installation via Custom Repository
1. In your Emby Dashboard, go to **Settings > Plugins > Repositories (+)**.
2. Add the custom repository URL:
   `https://andyciprian.github.io/Emby.Plugin.IntroDbApp/manifest.json`
3. Go to the **Catalog** tab, find **IntroDB.app**, click **Install**, and restart your server.

#### Option B: Manual Installation
1. Download `Emby.Plugin.IntroDbApp.zip` from our [GitHub Releases](https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp/releases/tag/v4.3.0).
2. Extract `Emby.Plugin.IntroDbApp.dll` into your Emby `plugins` folder.
3. Restart Emby Server.
4. Access **IntroDB.app** under the *Advanced* plugins menu.

---

### 🔗 Links:
- **GitHub Repository:** https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp
- **Latest Release:** https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp/releases/tag/v4.3.0
- **Upstream API:** https://introdb.app/

Feedback, device testing, and suggestions are warmly welcomed!
```

---

## Fase 3: Solicitud de Inclusión en la Tienda Oficial (Plugin Catalog)

Una vez que el hilo en el foro reciba comentarios positivos de otros usuarios durante 1 a 2 semanas:

### 1. Mensaje a los Desarrolladores Core de Emby
En el mismo hilo del foro, publica una respuesta mencionando a los líderes de desarrollo:
> *"@Luke @ebr The plugin has proven stable across multiple Emby 4.8+ setups with the new v4.3.0 release. We would love to have IntroDB.app added to the official Emby Plugin Catalog for one-click installation by our community users. Please let me know what details or manifest entries you require."*

### 2. Especificación del Paquete para `EmbyPackages.json`
El equipo de Emby registrará tu plugin en su catálogo central con los siguientes datos:

| Campo | Valor |
|---|---|
| **name** | `IntroDbApp` |
| **guid** | `f62e84d1-817a-4c48-8cf9-a6f951e73792` |
| **versionStr** | `4.3.0.0` |
| **classification** | `Release` |
| **category** | `Playback` (o `General`) |
| **description** | `Universal Intro, Credits & Post-Credits skip detection powered by IntroDB.app.` |
| **targetSystem** | `Server` |
| **targetVersion** | `>= 4.8.0.0` |
| **owner** | `Andy Ernesto Ciprian` |
| **sourceUrl** | `https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp/releases/download/v4.3.0/Emby.Plugin.IntroDbApp.zip` |

---

## Fase 4: Repositorio Comunitario Personalizado (Instalación con 1 Clic Inmediata)

El archivo `manifest.json` ya está creado y preparado en la carpeta `docs/manifest.json` de tu repositorio.

### ¿Cómo activarlo en GitHub Pages en 1 minuto?
1. Entra a tu repositorio: https://github.com/AndyCiprian/Emby.Plugin.IntroDbApp
2. Ve a **Settings > Pages** (menú lateral izquierdo).
3. En **Build and deployment > Source**, selecciona:
   - Branch: `master`
   - Folder: `/docs`
4. Haz clic en **Save**.
5. ¡Listo! En unos segundos tu repositorio de plugins estará activo en:
   `https://andyciprian.github.io/Emby.Plugin.IntroDbApp/manifest.json`

Cualquier usuario del mundo podrá agregar esa URL en **Emby > Configuración > Plugins > Repositorios (+)** e instalar o actualizar tu plugin con un solo clic directamente desde la interfaz gráfica de Emby.

---

## Flujo de Mantenimiento y Nuevas Actualizaciones

Para futuras versiones (por ejemplo `v4.3.1`):
1. Actualizas la versión en `Emby.Plugin.IntroDbApp.csproj`.
2. Haces commit y creas el nuevo tag:
   ```bash
   git tag v4.3.1
   git push origin master --tags
   ```
3. GitHub Actions compilará automáticamente el binario, creará el `.zip` y publicará la release en GitHub.
4. En `docs/manifest.json`, agregas el bloque con la nueva versión para que Emby notifique la actualización automática a todos los usuarios.
