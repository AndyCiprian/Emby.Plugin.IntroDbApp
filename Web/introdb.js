define([], function () {
    'use strict';

    var pluginId = "f62e84d1-817a-4c48-8cf9-a6f951e73792";
    var taskId = "7a6ff3e0ba408c92a7cb46dd9104d5c5";
    var globalStatsData = null;

    function cleanString(val) {
        if (!val) return "";
        return val.toString().trim().replace(/^["'“”«»]+|["'“”«»]+$/g, '').trim();
    }

    function setupTabs(page) {
        var tabBtns = [
            { btn: page.querySelector('#tabBtnConfig'), content: page.querySelector('#tabContentConfig') },
            { btn: page.querySelector('#tabBtnStats'), content: page.querySelector('#tabContentStats'), onLoad: function () { loadStats(page); } },
            { btn: page.querySelector('#tabBtnTools'), content: page.querySelector('#tabContentTools') },
            { btn: page.querySelector('#tabBtnLogs'), content: page.querySelector('#tabContentLogs'), onLoad: function () { loadLogs(page); } }
        ];

        tabBtns.forEach(function (tab) {
            if (!tab.btn || !tab.content) return;
            tab.btn.addEventListener('click', function () {
                tabBtns.forEach(function (t) {
                    if (t.btn && t.content) {
                        t.btn.classList.remove('active');
                        t.btn.style.background = 'rgba(128, 128, 128, 0.08)';
                        t.btn.style.color = 'inherit';
                        t.btn.style.borderColor = 'var(--line-background, rgba(128, 128, 128, 0.25))';
                        t.content.style.display = 'none';
                    }
                });
                tab.btn.classList.add('active');
                tab.btn.style.background = 'rgba(82, 181, 75, 0.2)';
                tab.btn.style.color = '#52B54B';
                tab.btn.style.borderColor = '#52B54B';
                tab.content.style.display = 'block';

                if (tab.onLoad) {
                    tab.onLoad();
                }
            });
        });
    }

    function setupMetricHelpButtons(page) {
        var helpMap = [
            { btn: '#btnHelpTimeSaved', banner: '#bannerHelpTimeSaved' },
            { btn: '#btnHelpIntros', banner: '#bannerHelpIntros' },
            { btn: '#btnHelpCredits', banner: '#bannerHelpCredits' },
            { btn: '#btnHelpIntroDb', banner: '#bannerHelpIntroDb' },
            { btn: '#btnHelpUserStats', banner: '#bannerHelpUserStats' },
            { btn: '#btnScanHelpToggle', banner: '#scanHelpBanner' }
        ];

        helpMap.forEach(function (item) {
            var btn = page.querySelector(item.btn);
            var banner = page.querySelector(item.banner);
            if (btn && banner) {
                btn.addEventListener('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    var isHidden = banner.style.display === "none" || !banner.style.display;
                    banner.style.display = isHidden ? "block" : "none";
                });
            }
        });
    }

    function updateModeDescription(mode, descEl) {
        if (!descEl) return;
        descEl.style.color = '#52B54B';
        switch (mode) {
            case "IntroDbPrimary":
                descEl.textContent = "Prioriza la sincronización con IntroDB.app. Los elementos sin registros en el proveedor principal conservan los marcadores provistos por TheIntroDB u orígenes locales.";
                break;
            case "Hybrid":
                descEl.textContent = "Prioriza la sincronización con TheIntroDB. IntroDB.app opera como proveedor complementario para la resolución de segmentos no detectados o ausentes.";
                break;
            case "Standalone":
                descEl.textContent = "Gestiona la indexación de segmentos exclusivamente a través de IntroDB.app, operando de manera independiente a cualquier otro proveedor.";
                break;
            case "Overwrite":
                descEl.textContent = "Fuerza la actualización y reemplazo sistemático de todos los marcadores existentes en la biblioteca utilizando los registros de IntroDB.app.";
                break;
        }
    }

    function renderApiKeyStatusUI(apiKey, isVerified, statusMsg, rateLimitRemaining, rateLimitLimit, lastChecked, page) {
        var badge = page.querySelector('#apiKeyBadge');
        var statusEl = page.querySelector('#statApiStatus');
        var rateLimitEl = page.querySelector('#statApiRateLimit');
        var lastCheckedEl = page.querySelector('#statApiLastChecked');

        var trimmedKey = cleanString(apiKey);

        if (!trimmedKey) {
            if (badge) {
                badge.style.display = "none";
                badge.textContent = "";
            }
            if (statusEl) {
                statusEl.textContent = "No configurada";
                statusEl.style.color = "";
            }
            if (rateLimitEl) rateLimitEl.textContent = "100 / 100 disponibles";
            if (lastCheckedEl) lastCheckedEl.textContent = "Nunca";
            return;
        }

        if (badge) badge.style.display = "inline-block";

        if (isVerified) {
            if (badge) {
                badge.textContent = "✓ Clave Verificada";
                badge.style.background = "rgba(82, 181, 75, 0.18)";
                badge.style.color = "#52B54B";
                badge.style.border = "1px solid #52B54B";
            }
            if (statusEl) {
                statusEl.textContent = "Activa y Verificada";
                statusEl.style.color = "#52B54B";
            }
        } else {
            if (badge) {
                badge.textContent = "⚠️ " + (statusMsg || "Clave no válida");
                badge.style.background = "rgba(229, 57, 53, 0.18)";
                badge.style.color = "#e53935";
                badge.style.border = "1px solid #e53935";
            }
            if (statusEl) {
                statusEl.textContent = statusMsg || "No válida o rechazada";
                statusEl.style.color = "#e53935";
            }
        }

        if (rateLimitEl && rateLimitRemaining !== undefined) {
            rateLimitEl.textContent = rateLimitRemaining + " / " + (rateLimitLimit || 100) + " disponibles";
        }
        if (lastCheckedEl && lastChecked) {
            lastCheckedEl.textContent = lastChecked;
        }
    }

    function setupApiKeyValidation(page) {
        var btnVerify = page.querySelector('#btnVerifyApiKey');
        var statusMsg = page.querySelector('#apiVerifyStatusMsg');
        var inputKey = page.querySelector('#txtApiKey');
        if (!btnVerify || !inputKey) return;

        // Escucha en tiempo real sobre el campo mientras el usuario escribe o limpia
        inputKey.addEventListener('input', function () {
            inputKey.style.borderColor = '';
            var currentKey = cleanString(inputKey.value);
            if (!currentKey) {
                page._isKeyCurrentlyVerified = false;
                renderApiKeyStatusUI("", false, "", 100, 100, "Nunca", page);
                if (statusMsg) statusMsg.textContent = "";
            } else if (page._verifiedKey && currentKey === page._verifiedKey) {
                page._isKeyCurrentlyVerified = true;
                renderApiKeyStatusUI(currentKey, true, page._verifiedKeyMessage || "Clave de API verificada y activa.", 100, 100, "Verificada", page);
                if (statusMsg) {
                    statusMsg.style.color = "#52B54B";
                    statusMsg.textContent = "✓ Clave de API verificada y activa.";
                }
            } else {
                page._isKeyCurrentlyVerified = false;
                renderApiKeyStatusUI(currentKey, false, "Pendiente de verificación", 100, 100, "Pendiente", page);
                if (statusMsg) {
                    statusMsg.style.color = "#ff9800";
                    statusMsg.textContent = "Presiona 'Verificar Clave' para validar la credencial con IntroDB.app.";
                }
            }
        });

        btnVerify.addEventListener('click', function () {
            var rawKey = inputKey.value;
            var key = cleanString(rawKey);
            inputKey.value = key; // Sanitizar visualmente en el campo

            if (!key) {
                inputKey.style.borderColor = '#e53935';
                inputKey.focus();
                if (statusMsg) {
                    statusMsg.style.color = '#e53935';
                    statusMsg.textContent = '⚠️ Campo obligatorio: Ingresa una clave de API antes de verificar.';
                }
                page._isKeyCurrentlyVerified = false;
                renderApiKeyStatusUI("", false, "", 100, 100, "Nunca", page);
                return;
            }

            inputKey.style.borderColor = '';
            if (statusMsg) {
                statusMsg.style.color = '#52B54B';
                statusMsg.textContent = 'Comprobando validez en vivo con api.introdb.app...';
            }

            // Usar fetch directo con encabezado de autenticación de Emby para garantizar parseo nativo JSON
            fetch(ApiClient.getUrl('IntroDbApp/ApiKey/Validate'), {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Emby-Token': ApiClient.accessToken()
                },
                body: JSON.stringify({ ApiKey: key })
            })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                return response.json();
            })
            .then(function (res) {
                var isValid = Boolean(res && (res.IsValid === true || res.isValid === true));
                var msgText = (res && (res.Message || res.message)) || (isValid ? 'Clave verificada y activa.' : 'Clave de API rechazada.');
                var rem = res ? (res.RateLimitRemaining !== undefined ? res.RateLimitRemaining : res.rateLimitRemaining) : 100;
                var lim = res ? (res.RateLimitLimit !== undefined ? res.RateLimitLimit : res.rateLimitLimit) : 100;
                var lastChk = res ? (res.LastChecked || res.lastChecked || 'Reciente') : 'Reciente';

                if (isValid) {
                    page._verifiedKey = key;
                    page._isKeyCurrentlyVerified = true;
                    page._verifiedKeyMessage = msgText;
                    if (statusMsg) {
                        statusMsg.style.color = '#52B54B';
                        statusMsg.textContent = '✓ ' + msgText;
                    }
                    renderApiKeyStatusUI(key, true, msgText, rem, lim, lastChk, page);
                } else {
                    page._verifiedKey = "";
                    page._isKeyCurrentlyVerified = false;
                    page._verifiedKeyMessage = msgText;
                    if (statusMsg) {
                        statusMsg.style.color = '#e53935';
                        statusMsg.textContent = '⚠️ ' + msgText;
                    }
                    renderApiKeyStatusUI(key, false, msgText, rem, lim, lastChk, page);
                }
            })
            .catch(function (err) {
                page._isKeyCurrentlyVerified = false;
                if (statusMsg) {
                    statusMsg.style.color = '#e53935';
                    statusMsg.textContent = 'Error de conexión con el servidor: ' + (err.message || err || 'desconocido');
                }
                renderApiKeyStatusUI(key, false, 'Error de red', 0, 0, "Error", page);
            });
        });
    }

    function loadConfiguration(page) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            var modeSelect = page.querySelector('#selectOperationMode');
            var modeDesc = page.querySelector('#modeDescription');
            if (modeSelect) {
                modeSelect.value = config.OperationMode || "IntroDbPrimary";
                updateModeDescription(modeSelect.value, modeDesc);
            }

            var chkEnabled = page.querySelector('#chkEnabled');
            if (chkEnabled) chkEnabled.checked = config.Enabled !== false;

            var chkRealtime = page.querySelector('#chkRealtimeScan');
            if (chkRealtime) chkRealtime.checked = config.RealtimeScanOnLibraryChange !== false;

            var chkOnDemand = page.querySelector('#chkOnDemand');
            if (chkOnDemand) chkOnDemand.checked = config.EnableOnDemandFetch !== false;

            var chkFetchIntros = page.querySelector('#chkFetchIntros');
            if (chkFetchIntros) chkFetchIntros.checked = config.FetchIntros !== false;

            var chkFetchCredits = page.querySelector('#chkFetchCredits');
            if (chkFetchCredits) chkFetchCredits.checked = config.FetchCredits !== false;

            var chkScanMovies = page.querySelector('#chkScanMovies');
            if (chkScanMovies) chkScanMovies.checked = config.ScanMovies !== false;

            var chkFetchPostCredits = page.querySelector('#chkFetchPostCredits');
            if (chkFetchPostCredits) chkFetchPostCredits.checked = config.FetchPostCredits !== false;
            
            var savedKey = cleanString(config.ApiKey);
            var txtKey = page.querySelector('#txtApiKey');
            if (txtKey) txtKey.value = savedKey;

            // Consultar estadísticas de la API en vivo
            ApiClient.getJSON(ApiClient.getUrl("IntroDbApp/Stats")).then(function (stats) {
                if (typeof stats === 'string') {
                    try { stats = JSON.parse(stats); } catch (e) {}
                }
                var isVerified = Boolean(stats && (stats.IsApiKeyVerified === true || stats.isApiKeyVerified === true) && savedKey);
                page._verifiedKey = isVerified ? savedKey : "";
                page._isKeyCurrentlyVerified = isVerified;
                page._verifiedKeyMessage = stats ? (stats.ApiKeyStatusMessage || stats.apiKeyStatusMessage) : "";

                renderApiKeyStatusUI(
                    savedKey,
                    isVerified,
                    page._verifiedKeyMessage,
                    stats ? (stats.ApiRateLimitRemaining !== undefined ? stats.ApiRateLimitRemaining : stats.apiRateLimitRemaining) : 100,
                    stats ? (stats.ApiRateLimitLimit !== undefined ? stats.ApiRateLimitLimit : stats.apiRateLimitLimit) : 100,
                    stats ? (stats.ApiKeyLastChecked || stats.apiKeyLastChecked) : "Nunca",
                    page
                );

                var statApiTotal = page.querySelector('#statApiTotal');
                if (statApiTotal) statApiTotal.textContent = ((stats && (stats.ApiSubmissionsTotal || stats.apiSubmissionsTotal)) || 0).toString();

                var statApiLive = page.querySelector('#statApiLive');
                if (statApiLive) statApiLive.textContent = ((stats && (stats.ApiSubmissionsLive || stats.apiSubmissionsLive)) || 0).toString();

                var statApiPending = page.querySelector('#statApiPending');
                if (statApiPending) statApiPending.textContent = ((stats && (stats.ApiSubmissionsPending || stats.apiSubmissionsPending)) || 0).toString();

                var statApiRejected = page.querySelector('#statApiRejected');
                if (statApiRejected) statApiRejected.textContent = ((stats && (stats.ApiSubmissionsRejected || stats.apiSubmissionsRejected)) || 0).toString();

                var total = (stats && (stats.ApiSubmissionsTotal || stats.apiSubmissionsTotal)) || 0;
                var live = (stats && (stats.ApiSubmissionsLive || stats.apiSubmissionsLive)) || 0;
                var rate = total > 0 ? Math.round((live / total) * 100) : 100;
                var statApiAcceptance = page.querySelector('#statApiAcceptance');
                if (statApiAcceptance) statApiAcceptance.textContent = rate + "%";

                Dashboard.hideLoadingMsg();
            }).catch(function () {
                Dashboard.hideLoadingMsg();
            });

        }).catch(function () {
            Dashboard.hideLoadingMsg();
        });
    }

    function saveConfiguration(page) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            var modeSelect = page.querySelector('#selectOperationMode');
            if (modeSelect) config.OperationMode = modeSelect.value;

            var chkEnabled = page.querySelector('#chkEnabled');
            if (chkEnabled) config.Enabled = chkEnabled.checked;

            var chkRealtime = page.querySelector('#chkRealtimeScan');
            if (chkRealtime) config.RealtimeScanOnLibraryChange = chkRealtime.checked;

            var chkOnDemand = page.querySelector('#chkOnDemand');
            if (chkOnDemand) config.EnableOnDemandFetch = chkOnDemand.checked;

            var chkFetchIntros = page.querySelector('#chkFetchIntros');
            if (chkFetchIntros) config.FetchIntros = chkFetchIntros.checked;

            var chkFetchCredits = page.querySelector('#chkFetchCredits');
            if (chkFetchCredits) config.FetchCredits = chkFetchCredits.checked;

            var chkScanMovies = page.querySelector('#chkScanMovies');
            if (chkScanMovies) config.ScanMovies = chkScanMovies.checked;

            var chkFetchPostCredits = page.querySelector('#chkFetchPostCredits');
            if (chkFetchPostCredits) config.FetchPostCredits = chkFetchPostCredits.checked;
            
            var keyVal = cleanString(page.querySelector('#txtApiKey').value);
            config.ApiKey = keyVal;

            if (!keyVal) {
                config.IsApiKeyVerified = false;
                config.ApiKeyStatusMessage = "No configurada";
            } else if (page._isKeyCurrentlyVerified && page._verifiedKey === keyVal) {
                config.IsApiKeyVerified = true;
                config.ApiKeyStatusMessage = page._verifiedKeyMessage || "Clave de API verificada y activa en IntroDB.app.";
            } else {
                config.IsApiKeyVerified = false;
                config.ApiKeyStatusMessage = "Clave no verificada. Presione 'Verificar Clave'.";
            }

            ApiClient.updatePluginConfiguration(pluginId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            }).catch(function () {
                Dashboard.hideLoadingMsg();
            });
        });
    }

    function formatTime(seconds) {
        var hours = Math.floor(seconds / 3600);
        var minutes = Math.floor((seconds % 3600) / 60);
        if (hours > 0) {
            return hours + "h " + minutes + "m";
        }
        return minutes + "m " + (seconds % 60) + "s";
    }

    function renderUserTable(users, tbody) {
        if (!tbody) return;
        if (!users || !users.length) {
            tbody.innerHTML = '<tr><td colspan="5" style="padding: 15px; text-align: center; opacity: 0.6;">Aún no hay reproducciones registradas por usuarios.</td></tr>';
            return;
        }

        var html = '';
        users.forEach(function (u) {
            var lastAct = u.LastActivity ? new Date(u.LastActivity).toLocaleString() : 'Reciente';
            html += '<tr style="border-bottom: 1px solid var(--line-background, rgba(128, 128, 128, 0.15));">';
            html += '<td style="padding: 10px 14px; font-weight: bold; color: #52B54B;">' + (u.UserName || 'Usuario') + '</td>';
            html += '<td style="padding: 10px 14px; color: #52B54B;">' + (u.IntrosSkipped || 0) + '</td>';
            html += '<td style="padding: 10px 14px; color: #ab47bc;">' + (u.OutrosSkipped || 0) + '</td>';
            html += '<td style="padding: 10px 14px; font-weight: 500; color: #00bcd4;">' + formatTime(u.SecondsSaved || 0) + '</td>';
            html += '<td style="padding: 10px 14px; opacity: 0.85; font-size: 0.88em;">' + lastAct + '</td>';
            html += '</tr>';
        });
        tbody.innerHTML = html;
    }

    function loadStats(page) {
        ApiClient.getJSON(ApiClient.getUrl("IntroDbApp/Stats")).then(function (data) {
            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) {}
            }
            globalStatsData = data;

            var timeEl = page.querySelector('#statTimeSaved');
            if (timeEl) timeEl.textContent = formatTime(data.EstimatedSecondsSaved || 0);

            var totalEps = data.TotalLibraryEpisodes || 0;
            var introsCovered = data.TotalEpisodesWithIntro || 0;
            var creditsCovered = data.TotalEpisodesWithCredits || 0;

            var introPct = totalEps > 0 ? Math.round((introsCovered / totalEps) * 100) : 0;
            var creditPct = totalEps > 0 ? Math.round((creditsCovered / totalEps) * 100) : 0;

            var introsCovEl = page.querySelector('#statIntrosCovered');
            if (introsCovEl) introsCovEl.textContent = introPct + "%";

            var introsCntEl = page.querySelector('#statIntrosCount');
            if (introsCntEl) introsCntEl.textContent = introsCovered + " / " + totalEps + " episodios";

            var credCovEl = page.querySelector('#statCreditsCovered');
            if (credCovEl) credCovEl.textContent = creditPct + "%";

            var credCntEl = page.querySelector('#statCreditsCount');
            if (credCntEl) credCntEl.textContent = creditsCovered + " / " + totalEps + " episodios";

            var totalRescued = (data.AddedIntrosCount || 0) + (data.AddedCreditsCount || 0) + (data.AddedPostCreditsCount || 0);
            var rescEl = page.querySelector('#statIntroDbRescued');
            if (rescEl) rescEl.textContent = "+" + totalRescued;

            var rescDetEl = page.querySelector('#statIntroDbDetail');
            if (rescDetEl) rescDetEl.textContent = (data.AddedIntrosCount || 0) + " intros · " + (data.AddedCreditsCount || 0) + " créditos";

            // Datos estructurados del último escaneo
            var scanStateEl = page.querySelector('#statScanState');
            if (scanStateEl) scanStateEl.textContent = data.LastScanState || "No ejecutado";

            var scanDateEl = page.querySelector('#statScanDate');
            if (scanDateEl) scanDateEl.textContent = data.LastScanDate || "Nunca";

            var scanDurEl = page.querySelector('#statScanDuration');
            if (scanDurEl) scanDurEl.textContent = data.LastScanDuration || "0 s";

            var scanItemsEl = page.querySelector('#statLastScannedItems');
            if (scanItemsEl) scanItemsEl.textContent = (data.ScannedItemsCount || 0).toString();

            var cached404El = page.querySelector('#statCached404');
            if (cached404El) cached404El.textContent = (data.Cached404Count || 0).toString();

            var userSelect = page.querySelector('#selectUserFilter');
            if (userSelect) {
                userSelect.innerHTML = '<option value="all">🌐 Todos los Perfiles (Global)</option>';
                var users = data.UserMetrics || [];
                users.forEach(function (u) {
                    var opt = document.createElement('option');
                    opt.value = u.UserId;
                    opt.textContent = "👤 " + (u.UserName || "Usuario");
                    userSelect.appendChild(opt);
                });
            }

            var tbody = page.querySelector('#userMetricsTableBody');
            renderUserTable(data.UserMetrics || [], tbody);
        });
    }

    function setupUserFilter(page) {
        var userFilter = page.querySelector('#selectUserFilter');
        if (!userFilter) return;
        userFilter.addEventListener('change', function () {
            if (!globalStatsData) return;
            var val = userFilter.value;
            var timeEl = page.querySelector('#statTimeSaved');
            var detailEl = page.querySelector('#statTimeDetail');
            if (val === "all") {
                if (timeEl) timeEl.textContent = formatTime(globalStatsData.EstimatedSecondsSaved || 0);
                if (detailEl) detailEl.textContent = "Openings y Endings omitidos (Global)";
            } else {
                var user = (globalStatsData.UserMetrics || []).find(function (u) { return u.UserId === val; });
                if (user) {
                    if (timeEl) timeEl.textContent = formatTime(user.SecondsSaved || 0);
                    if (detailEl) detailEl.textContent = (user.IntrosSkipped || 0) + " intros · " + (user.OutrosSkipped || 0) + " créditos (" + user.UserName + ")";
                }
            }
        });
    }

    function loadLogs(page) {
        var tbody = page.querySelector('#logsTableBody');
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="4" style="padding: 15px; text-align: center;">Consultando registros...</td></tr>';

        ApiClient.getJSON(ApiClient.getUrl("IntroDbApp/Logs")).then(function (logs) {
            if (typeof logs === 'string') {
                try { logs = JSON.parse(logs); } catch (e) {}
            }
            if (!logs || !logs.length) {
                tbody.innerHTML = '<tr><td colspan="4" style="padding: 15px; text-align: center; opacity: 0.6;">No hay eventos registrados recientemente.</td></tr>';
                return;
            }

            var html = '';
            logs.forEach(function (log) {
                var time = log.Timestamp ? new Date(log.Timestamp).toLocaleTimeString() : "";
                
                var dot = "🟢";
                var actionColor = "#52B54B";
                if (log.Level === "warn") {
                    dot = "🟡";
                    actionColor = "#ffd54f";
                } else if (log.Level === "error") {
                    dot = "🔴";
                    actionColor = "#ef5350";
                }

                html += '<tr style="border-bottom: 1px solid var(--line-background, rgba(128, 128, 128, 0.15));">';
                html += '<td style="padding: 10px 14px; opacity: 0.85; font-family: monospace; white-space: nowrap;">' + time + '</td>';
                html += '<td style="padding: 10px 14px; font-weight: 500;">' + (log.Title || "") + '</td>';
                html += '<td style="padding: 10px 14px; font-weight: 600; color: ' + actionColor + ';">' + dot + ' ' + (log.Action || "") + '</td>';
                html += '<td style="padding: 10px 14px; opacity: 0.9; font-size: 0.92em;">' + (log.Details || "") + '</td>';
                html += '</tr>';
            });
            tbody.innerHTML = html;
        }).catch(function () {
            tbody.innerHTML = '<tr><td colspan="4" style="padding: 15px; text-align: center; color: #f44336;">Error al cargar registros.</td></tr>';
        });
    }

    function setupTools(page) {
        var msg = page.querySelector('#actionStatusMsg');

        // Botón 1: Escaneo General de Biblioteca
        var btnScan = page.querySelector('#btnRunScanNow');
        if (btnScan) {
            btnScan.addEventListener('click', function () {
                if (msg) {
                    msg.style.color = '#52B54B';
                    msg.textContent = 'Iniciando escaneo general de biblioteca en segundo plano...';
                }
                ApiClient.ajax({
                    type: 'POST',
                    url: ApiClient.getUrl('ScheduledTasks/Running/' + taskId)
                }).then(function () {
                    if (msg) {
                        msg.style.color = '#52B54B';
                        msg.textContent = '✓ ¡Escaneo general iniciado con éxito! El motor está procesando todos los medios en segundo plano. Puedes seguir el avance en el Registro de Eventos.';
                    }
                    loadLogs(page);
                }).catch(function () {
                    if (msg) {
                        msg.style.color = '#00bcd4';
                        msg.textContent = 'ℹ️ La tarea de escaneo general ya se encuentra en ejecución en segundo plano.';
                    }
                });
            });
        }

        // Botón 2: Limpieza de Caché 404
        var btnClearCache = page.querySelector('#btnClear404Cache');
        if (btnClearCache) {
            btnClearCache.addEventListener('click', function () {
                if (msg) {
                    msg.style.color = '#52B54B';
                    msg.textContent = 'Comprobando y depurando memoria caché de ausencias...';
                }
                fetch(ApiClient.getUrl('IntroDbApp/Cache/Clear'), {
                    method: 'POST',
                    headers: { 'X-Emby-Token': ApiClient.accessToken() }
                })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    var count = res ? (res.ClearedCount || res.clearedCount || 0) : 0;
                    var alreadyEmpty = Boolean(res && (res.AlreadyEmpty === true || res.alreadyEmpty === true));

                    if (alreadyEmpty || count === 0) {
                        if (msg) {
                            msg.style.color = '#00bcd4';
                            msg.textContent = 'ℹ️ La memoria caché de no encontrados (404) ya se encuentra completamente limpia (0 elementos guardados).';
                        }
                    } else {
                        if (msg) {
                            msg.style.color = '#52B54B';
                            msg.textContent = '✓ Memoria caché liberada con éxito. Se eliminaron ' + count + ' registros de no encontrados (404). Ahora pueden volver a consultarse.';
                        }
                    }
                    loadStats(page);
                }).catch(function (err) {
                    if (msg) {
                        msg.style.color = '#e53935';
                        msg.textContent = 'Error al limpiar caché: ' + (err.message || err || 'desconocido');
                    }
                });
            });
        }

        // Botón 3: Escaneo Específico de Título por IMDb ID
        var txtQuickScan = page.querySelector('#txtQuickScanImdb');
        var btnQuickScan = page.querySelector('#btnQuickScan');
        var quickScanMsg = page.querySelector('#quickScanMsg');

        if (btnQuickScan && txtQuickScan) {
            btnQuickScan.addEventListener('click', function () {
                var imdb = cleanString(txtQuickScan.value);
                if (!imdb) {
                    txtQuickScan.style.borderColor = '#e53935';
                    txtQuickScan.focus();
                    if (quickScanMsg) {
                        quickScanMsg.style.color = '#e53935';
                        quickScanMsg.textContent = '⚠️ Campo obligatorio: Ingresa un identificador IMDb (ejemplo: tt0903747) para escanear ese título.';
                    }
                    return;
                }

                txtQuickScan.style.borderColor = '';
                if (quickScanMsg) {
                    quickScanMsg.style.color = '#52B54B';
                    quickScanMsg.textContent = 'Consultando IntroDB e inyectando marcas para ' + imdb + '...';
                }

                fetch(ApiClient.getUrl('IntroDbApp/ScanItem'), {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Emby-Token': ApiClient.accessToken()
                    },
                    body: JSON.stringify({ ImdbId: imdb })
                })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    var success = Boolean(res && (res.Success === true || res.success === true));
                    var msgText = (res && (res.Message || res.message)) || 'Escaneo completado.';
                    
                    if (quickScanMsg) {
                        quickScanMsg.style.color = success ? '#52B54B' : '#ff9800';
                        quickScanMsg.textContent = (success ? '✓ ' : '⚠️ ') + msgText;
                    }
                    loadStats(page);
                    loadLogs(page);
                }).catch(function (err) {
                    if (quickScanMsg) {
                        quickScanMsg.style.color = '#e53935';
                        quickScanMsg.textContent = 'Error en el escaneo: ' + (err.message || err || 'desconocido');
                    }
                });
            });
        }

        // Botón 4: Centro de Diagnóstico en Emby
        var txtDiagImdb = page.querySelector('#txtDiagImdb');
        var btnDiagnose = page.querySelector('#btnDiagnoseItem');
        var diagMsg = page.querySelector('#diagStatusMsg');
        var diagPre = page.querySelector('#diagResultPre');

        if (btnDiagnose && txtDiagImdb) {
            btnDiagnose.addEventListener('click', function () {
                var imdb = cleanString(txtDiagImdb.value);
                if (!imdb) {
                    txtDiagImdb.style.borderColor = '#e53935';
                    txtDiagImdb.focus();
                    if (diagMsg) {
                        diagMsg.style.color = '#e53935';
                        diagMsg.textContent = '⚠️ Campo obligatorio: Ingresa un identificador IMDb (ejemplo: tt10431290) para diagnosticar en Emby.';
                    }
                    return;
                }

                txtDiagImdb.style.borderColor = '';
                if (diagMsg) {
                    diagMsg.style.color = '#52B54B';
                    diagMsg.textContent = 'Diagnosticando base de datos interna de Emby para ' + imdb + '...';
                }
                if (diagPre) diagPre.style.display = 'none';

                fetch(ApiClient.getUrl('IntroDbApp/DiagnoseItem', { ImdbId: imdb }), {
                    headers: { 'X-Emby-Token': ApiClient.accessToken() }
                })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    if (diagPre) {
                        diagPre.style.display = 'block';
                        diagPre.textContent = JSON.stringify(res, null, 2);
                    }
                    var success = Boolean(res && (res.Success === true || res.success === true));
                    if (diagMsg) {
                        diagMsg.style.color = success ? '#52B54B' : '#ff9800';
                        diagMsg.textContent = success 
                            ? '✓ Diagnóstico finalizado para: ' + (res.Title || imdb) + ' (' + (res.Type || 'Título') + '). Marcadores Intro: ' + (res.HasIntroMarker ? 'SÍ' : 'NO') + ' | Créditos: ' + (res.HasCreditsMarker ? 'SÍ' : 'NO')
                            : ('⚠️ ' + (res.Message || 'No encontrado'));
                    }
                })
                .catch(function (err) {
                    if (diagMsg) {
                        diagMsg.style.color = '#e53935';
                        diagMsg.textContent = 'Error al ejecutar diagnóstico: ' + (err.message || err || 'desconocido');
                    }
                });
            });
        }

        // Botón 5: Pruebas de Simulación de Reproducción en Vivo
        var btnSimIntro = page.querySelector('#btnSimulateIntro');
        var btnSimOutro = page.querySelector('#btnSimulateOutro');
        var simMsg = page.querySelector('#simStatusMsg');

        function runSimulateSkip(isIntro, seconds) {
            var imdb = cleanString(txtDiagImdb ? txtDiagImdb.value : "");
            if (!imdb) {
                if (txtDiagImdb) {
                    txtDiagImdb.style.borderColor = '#e53935';
                    txtDiagImdb.focus();
                }
                if (simMsg) {
                    simMsg.style.color = '#e53935';
                    simMsg.textContent = '⚠️ Campo requerido: Ingresa un identificador IMDb (ejemplo: tt10431290) en el campo superior antes de simular para asociar la reproducción a un título real de tu colección.';
                }
                return;
            }
            if (txtDiagImdb) txtDiagImdb.style.borderColor = '';

            if (simMsg) {
                simMsg.style.color = '#52B54B';
                simMsg.textContent = 'Simulando evento de salto (' + (isIntro ? 'Intro' : 'Créditos') + ') para ' + imdb + '...';
            }

            fetch(ApiClient.getUrl('IntroDbApp/SimulateSkip'), {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Emby-Token': ApiClient.accessToken()
                },
                body: JSON.stringify({ IsIntro: isIntro, Seconds: seconds, ImdbId: imdb })
            })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                var success = Boolean(res && (res.Success === true || res.success === true));
                var msgText = (res && (res.Message || res.message)) || 'Salto simulado con éxito.';
                if (simMsg) {
                    simMsg.style.color = success ? '#52B54B' : '#e53935';
                    simMsg.textContent = (success ? '✓ ' : '⚠️ ') + msgText;
                }
                if (success) {
                    loadStats(page);
                    loadLogs(page);
                }
            })
            .catch(function (err) {
                if (simMsg) {
                    simMsg.style.color = '#e53935';
                    simMsg.textContent = 'Error en la simulación: ' + (err.message || err || 'desconocido');
                }
            });
        }

        if (btnSimIntro) {
            btnSimIntro.addEventListener('click', function () { runSimulateSkip(true, 85); });
        }
        if (btnSimOutro) {
            btnSimOutro.addEventListener('click', function () { runSimulateSkip(false, 90); });
        }

        // Alternancia de campos según Tipo de Medio en Inspector
        var inspectType = page.querySelector('#inspectMediaType');
        var inspectSeasonField = page.querySelector('#inspectFieldSeason');
        var inspectEpisodeField = page.querySelector('#inspectFieldEpisode');
        if (inspectType && inspectSeasonField && inspectEpisodeField) {
            inspectType.addEventListener('change', function () {
                var isMovie = inspectType.value === "movie";
                inspectSeasonField.style.display = isMovie ? "none" : "";
                inspectEpisodeField.style.display = isMovie ? "none" : "";
            });
        }

        // Inspector de Metadatos con Validación Obligatoria
        var inputInspectImdb = page.querySelector('#inspectImdbId');
        var inspectValidationMsg = page.querySelector('#inspectValidationMsg');
        var btnInspect = page.querySelector('#btnInspectApi');
        if (btnInspect && inputInspectImdb) {
            btnInspect.addEventListener('click', function () {
                var isMovie = inspectType ? inspectType.value === "movie" : false;
                var imdb = cleanString(inputInspectImdb.value);
                
                // Fallback automático al placeholder si el campo queda en blanco
                var seasonInput = page.querySelector('#inspectSeason');
                var epInput = page.querySelector('#inspectEpisode');
                var season = seasonInput && seasonInput.value.trim() ? seasonInput.value.trim() : "1";
                var ep = epInput && epInput.value.trim() ? epInput.value.trim() : "1";
                var out = page.querySelector('#inspectResult');

                if (!imdb) {
                    inputInspectImdb.style.borderColor = '#e53935';
                    inputInspectImdb.focus();
                    if (inspectValidationMsg) {
                        inspectValidationMsg.style.color = '#e53935';
                        inspectValidationMsg.textContent = '⚠️ Campo obligatorio: Debes ingresar un identificador IMDb (ejemplo: tt0903747) para realizar la consulta.';
                    }
                    if (out) out.textContent = "Por favor ingresa un identificador IMDb válido.";
                    return;
                }

                inputInspectImdb.style.borderColor = '';
                if (inspectValidationMsg) {
                    if (!isMovie && (!seasonInput.value.trim() || !epInput.value.trim())) {
                        inspectValidationMsg.style.color = '#00bcd4';
                        inspectValidationMsg.textContent = 'ℹ️ Consultando por defecto Temporada ' + season + ', Episodio ' + ep + ' (marca de agua). Puedes ingresar otros números si lo deseas.';
                    } else {
                        inspectValidationMsg.textContent = '';
                    }
                }
                if (out) out.textContent = "Consultando api.introdb.app...";

                var inspectParams = {
                    ImdbId: imdb,
                    IsMovie: isMovie
                };
                if (!isMovie) {
                    inspectParams.Season = season;
                    inspectParams.Episode = ep;
                }

                var inspectUrl = ApiClient.getUrl('IntroDbApp/Inspect', inspectParams);

                ApiClient.getJSON(inspectUrl).then(function (result) {
                    if (typeof result === 'string') {
                        try { result = JSON.parse(result); } catch (e) {}
                    }
                    if (out) out.textContent = JSON.stringify(result, null, 2);
                }).catch(function (err) {
                    if (out) out.textContent = "Error al consultar: " + JSON.stringify(err);
                });
            });
        }

        // Contribución Comunitaria con Validación Obligatoria
        var subType = page.querySelector('#subMediaType');
        var subSeasonField = page.querySelector('#subFieldSeason');
        var subEpisodeField = page.querySelector('#subFieldEpisode');
        var subSegmentType = page.querySelector('#subSegmentType');
        var subImdbInput = page.querySelector('#subImdbId');

        if (subType && subSegmentType) {
            subType.addEventListener('change', function () {
                var isMovie = subType.value === "movie";
                if (subSeasonField) subSeasonField.style.display = isMovie ? "none" : "";
                if (subEpisodeField) subEpisodeField.style.display = isMovie ? "none" : "";

                if (isMovie) {
                    subSegmentType.innerHTML = '<option value="outro">Créditos Finales (End Credits)</option><option value="post_credits">Escena Post-Créditos</option>';
                } else {
                    subSegmentType.innerHTML = '<option value="intro">Intro (Opening)</option><option value="outro">Créditos (Ending)</option><option value="recap">Recap (Resumen)</option><option value="post_credits">Escena Post-Créditos</option>';
                }
            });
        }

        var btnSubmit = page.querySelector('#btnSubmitSegment');
        var subMsg = page.querySelector('#subStatusMsg');
        if (btnSubmit && subImdbInput) {
            btnSubmit.addEventListener('click', function () {
                var isMovie = subType ? subType.value === "movie" : false;
                var imdb = cleanString(subImdbInput.value);
                var segType = subSegmentType ? subSegmentType.value : "intro";
                
                // Fallback automático al placeholder si el campo queda en blanco
                var subSeasonInput = page.querySelector('#subSeason');
                var subEpInput = page.querySelector('#subEpisode');
                var season = parseInt(subSeasonInput && subSeasonInput.value.trim() ? subSeasonInput.value.trim() : "1", 10) || 1;
                var ep = parseInt(subEpInput && subEpInput.value.trim() ? subEpInput.value.trim() : "1", 10) || 1;

                var startSec = parseFloat(page.querySelector('#subStartSec').value);
                var endSec = parseFloat(page.querySelector('#subEndSec').value);

                if (!imdb) {
                    subImdbInput.style.borderColor = '#e53935';
                    subImdbInput.focus();
                    if (subMsg) {
                        subMsg.style.color = '#e53935';
                        subMsg.textContent = '⚠️ Campo obligatorio: Ingresa el identificador IMDb (ejemplo: tt0903747).';
                    }
                    return;
                }
                subImdbInput.style.borderColor = '';

                if (isNaN(startSec) || isNaN(endSec) || endSec <= startSec) {
                    if (subMsg) {
                        subMsg.style.color = '#e53935';
                        subMsg.textContent = '⚠️ Campos obligatorios: Debes ingresar marcas de tiempo válidas en segundos (Fin debe ser mayor a Inicio).';
                    }
                    return;
                }

                if (subMsg) {
                    subMsg.style.color = '#52B54B';
                    subMsg.textContent = 'Remitiendo segmento a IntroDB.app...';
                }

                var payload = {
                    ImdbId: imdb,
                    IsMovie: isMovie,
                    SegmentType: segType,
                    StartSec: startSec,
                    EndSec: endSec
                };
                if (!isMovie) {
                    payload.Season = season;
                    payload.Episode = ep;
                }

                fetch(ApiClient.getUrl('IntroDbApp/Submit'), {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Emby-Token': ApiClient.accessToken()
                    },
                    body: JSON.stringify(payload)
                })
                .then(function (r) { return r.json(); })
                .then(function (res) {
                    var success = Boolean(res && (res.Success === true || res.success === true));
                    var msgText = (res && (res.Message || res.message)) || 'Envío procesado.';
                    if (subMsg) {
                        subMsg.style.color = success ? '#52B54B' : '#e53935';
                        subMsg.textContent = (success ? '✓ ' : 'Error: ') + msgText;
                    }
                    if (success) {
                        subImdbInput.value = '';
                        var startInput = page.querySelector('#subStartSec');
                        if (startInput) startInput.value = '';
                        var endInput = page.querySelector('#subEndSec');
                        if (endInput) endInput.value = '';
                        if (subSeasonInput) subSeasonInput.value = '';
                        if (subEpInput) subEpInput.value = '';
                        loadConfiguration(page);
                    }
                })
                .catch(function (err) {
                    if (subMsg) {
                        subMsg.style.color = '#e53935';
                        subMsg.textContent = 'Error en la petición: ' + (err.message || err || 'desconocido');
                    }
                });
            });
        }
    }

    return function (view, params) {
        view.addEventListener('viewshow', function () {
            if (!view._isIntroDbBound) {
                view._isIntroDbBound = true;
                setupTabs(view);
                setupMetricHelpButtons(view);
                setupTools(view);
                setupUserFilter(view);
                setupApiKeyValidation(view);

                var modeSelect = view.querySelector('#selectOperationMode');
                var modeDesc = view.querySelector('#modeDescription');
                if (modeSelect) {
                    modeSelect.addEventListener('change', function () {
                        updateModeDescription(modeSelect.value, modeDesc);
                    });
                }

                var btnRefreshLogs = view.querySelector('#btnRefreshLogs');
                if (btnRefreshLogs) {
                    btnRefreshLogs.addEventListener('click', function () {
                        loadLogs(view);
                    });
                }

                var btnClearLogs = view.querySelector('#btnClearLogs');
                if (btnClearLogs) {
                    btnClearLogs.addEventListener('click', function () {
                        fetch(ApiClient.getUrl('IntroDbApp/Logs/Clear'), {
                            method: 'POST',
                            headers: { 'X-Emby-Token': ApiClient.accessToken() }
                        }).then(function () {
                            loadLogs(view);
                        });
                    });
                }

                var form = view.querySelector('#IntroDbConfigForm');
                if (form) {
                    form.addEventListener('submit', function (e) {
                        e.preventDefault();
                        saveConfiguration(view);
                        return false;
                    });
                }
            }

            loadConfiguration(view);
        });
    };
});
