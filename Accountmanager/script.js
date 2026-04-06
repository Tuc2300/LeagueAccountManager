let accounts = [];
let currentEditId = null;
let deleteId = null;

// DOM-Elemente
const accountList = document.getElementById('accountList');
const searchInput = document.getElementById('searchInput');
const addBtn = document.getElementById('addBtn');
const accountModal = document.getElementById('accountModal');
const deleteModal = document.getElementById('deleteModal');
const modalTitle = document.getElementById('modalTitle');
const inputName = document.getElementById('inputName');
const inputTag = document.getElementById('inputTag');
const inputUsername = document.getElementById('inputUsername');
const inputPassword = document.getElementById('inputPassword');
const inputRegion = document.getElementById('inputRegion');
const toast = document.getElementById('toast');
const toastMessage = document.getElementById('toastMessage');
const toastIcon = document.getElementById('toastIcon');

// WebView2 Kommunikation mit C#
function sendMessageToCSharp(action, data = null) {
    return new Promise((resolve, reject) => {
        const message = { action, data };
        const messageId = Date.now();

        window.chrome.webview.postMessage({ ...message, messageId });

        // Listener für Response
        const handler = (event) => {
            const response = event.data;
            if (response.messageId === messageId) {
                window.chrome.webview.removeEventListener('message', handler);
                if (response.success) {
                    resolve(response.data);
                } else {
                    reject(new Error(response.error || 'Unbekannter Fehler'));
                }
            }
        };

        window.chrome.webview.addEventListener('message', handler);

        // Timeout nach 5 Sekunden
        setTimeout(() => {
            window.chrome.webview.removeEventListener('message', handler);
            reject(new Error('Timeout'));
        }, 5000);
    });
}

// Backend-API Funktionen
async function loadAccounts() {
    try {
        const data = await sendMessageToCSharp('GET_ACCOUNTS');
        accounts = data;
        renderAccounts();
    } catch (error) {
        showToast('Fehler beim Laden der Accounts: ' + error.message, 'error');
        console.error(error);
    }
}

async function createAccount(accountData) {
    try {
        const newAccount = await sendMessageToCSharp('CREATE_ACCOUNT', accountData);
        accounts.push(newAccount);
        renderAccounts();
        showToast('Account erfolgreich erstellt!', 'success');
    } catch (error) {
        showToast('Fehler beim Erstellen: ' + error.message, 'error');
        console.error(error);
    }
}

async function updateAccount(id, accountData) {
    try {
        const updatedAccount = await sendMessageToCSharp('UPDATE_ACCOUNT', { id, ...accountData });
        const index = accounts.findIndex(acc => acc.id === id);
        if (index !== -1) accounts[index] = updatedAccount;
        renderAccounts();
        showToast('Account erfolgreich aktualisiert!', 'success');
    } catch (error) {
        showToast('Fehler beim Aktualisieren: ' + error.message, 'error');
        console.error(error);
    }
}

async function deleteAccountById(id) {
    try {
        await sendMessageToCSharp('DELETE_ACCOUNT', { id });
        accounts = accounts.filter(acc => acc.id !== id);
        renderAccounts();
        showToast('Account erfolgreich gelöscht!', 'success');
    } catch (error) {
        showToast('Fehler beim Löschen: ' + error.message, 'error');
        console.error(error);
    }
}
function renderAccounts(accountsToRender = accounts) {
    accountList.innerHTML = '';

    if (accountsToRender.length === 0) {
        accountList.innerHTML = `
            <div class="empty-state">
                <p>Keine Accounts gefunden</p>
                <i class="fas fa-inbox" style="font-size: 64px; margin-top: 20px;"></i>
            </div>
        `;
        return;
    }

    accountsToRender.forEach(account => {
        const item = document.createElement('div');
        item.className = 'account-item';
        item.innerHTML = `
            <button class="btn-auto-login" onclick="autoLogin(${account.id})" title="Automatisch einloggen">
                <i class="fas fa-play"></i>
                <span>Auto Login</span>
            </button>
            
            <div class="account-info">
                <h3>
                    <img src="LeagueIcon.png" alt="League" style="width: 24px; height: 24px; object-fit: contain;">
                    ${account.name}<span class="account-tag">#${account.tag}</span>
                </h3>
                <div class="account-credentials">
                    <div class="credential-field">
                        <i class="fas fa-user"></i>
                        <span>${account.username}</span>
                    </div>
                    <div class="credential-field password-field">
                        <i class="fas fa-lock"></i>
                        <span class="password-text" id="pass-${account.id}">••••••••</span>
                        <button class="password-toggle" onclick="togglePassword(${account.id})">
                            <i class="fas fa-eye" id="eye-${account.id}"></i>
                        </button>
                    </div>
                    <div class="credential-field">
                        <i class="fas fa-globe"></i>
                        <span>${getRegionName(account.region)}</span>
                    </div>
                </div>
            </div>
            <div class="account-actions">
                <button class="btn-action btn-opgg" onclick="openOpGG('${account.region}', '${account.name}', '${account.tag}')" title="OP.GG öffnen">
                    <i class="fas fa-chart-line"></i>
                    <span class="btn-text">OP.GG</span>
                </button>
                <button class="btn-action btn-copy-username" onclick="copyUsernameOnly(${account.id})" title="Nur Name kopieren">
                    <i class="fas fa-user"></i>
                    <span class="btn-text">Name</span>
                </button>
                <button class="btn-action btn-copy-username" onclick="copyUsername(${account.id})" title="Name + Tag kopieren">
                    <i class="fas fa-user-tag"></i>
                    <span class="btn-text">Name + Tag</span>
                </button>
                <button class="btn-action btn-copy" onclick="copyPassword(${account.id})" title="Passwort kopieren">
                    <i class="fas fa-copy"></i>
                    <span class="btn-text">Passwort</span>
                </button>
                <button class="btn-action btn-edit" onclick="editAccount(${account.id})" title="Bearbeiten">
                    <i class="fas fa-edit"></i>
                    <span class="btn-text">Bearbeiten</span>
                </button>
                <button class="btn-action btn-delete" onclick="deleteAccount(${account.id})" title="Löschen">
                    <i class="fas fa-trash"></i>
                    <span class="btn-text">Löschen</span>
                </button>
            </div>
        `;
        accountList.appendChild(item);
    });
}


// Region-Namen für Anzeige
function getRegionName(region) {
    const regionMap = {
        'euw': 'EU West',
        'eune': 'EU Nordic & East',
        'na': 'North America',
        'kr': 'Korea',
        'jp': 'Japan',
        'br': 'Brazil',
        'lan': 'LAN',
        'las': 'LAS',
        'oce': 'Oceania',
        'tr': 'Turkey',
        'ru': 'Russia'
    };
    return regionMap[region] || region;
}

// Auto-Login Funktion
async function autoLogin(id) {
    const account = accounts.find(acc => acc.id === id);

    if (!account) {
        showToast('Account nicht gefunden!', 'error');
        return;
    }

    try {
        openLoginProgress();

        await sendMessageToCSharp('AUTO_LOGIN', {
            id: account.id,
            username: account.username,
            password: account.password,
            region: account.region
        });
    } catch (error) {
        showToast('Fehler beim Auto-Login: ' + error.message, 'error');
        console.error(error);
    }
}

// Login Progress Modal
function openLoginProgress() {
    for (let i = 1; i <= 3; i++) {
        const step = document.getElementById('loginStep' + i);
        step.setAttribute('data-status', 'waiting');
        step.querySelector('.step-icon').innerHTML = '<i class="fas fa-circle"></i>';
    }
    document.getElementById('loginStatusMessage').textContent = '';
    document.getElementById('loginProgressClose').style.display = 'none';
    document.getElementById('loginProgressCancel').style.display = '';
    document.getElementById('loginProgressModal').classList.add('show');
}

function closeLoginProgress() {
    document.getElementById('loginProgressModal').classList.remove('show');
}

function cancelLoginProgress() {
    sendMessageToCSharp('CANCEL_AUTO_LOGIN').catch(() => {});
    closeLoginProgress();
}

function updateLoginProgress(data) {
    const { step, totalSteps, message, status } = data;

    // Update all steps
    for (let i = 1; i <= totalSteps; i++) {
        const stepEl = document.getElementById('loginStep' + i);
        if (!stepEl) continue;

        if (status === 'error') {
            if (i === step || (step === 0 && i === 1)) {
                stepEl.setAttribute('data-status', 'error');
                stepEl.querySelector('.step-icon').innerHTML = '<i class="fas fa-times-circle"></i>';
            }
        } else if (i < step) {
            stepEl.setAttribute('data-status', 'done');
            stepEl.querySelector('.step-icon').innerHTML = '<i class="fas fa-check-circle"></i>';
        } else if (i === step) {
            if (status === 'done') {
                stepEl.setAttribute('data-status', 'done');
                stepEl.querySelector('.step-icon').innerHTML = '<i class="fas fa-check-circle"></i>';
            } else {
                stepEl.setAttribute('data-status', 'active');
                stepEl.querySelector('.step-icon').innerHTML = '<i class="fas fa-circle-notch fa-spin"></i>';
            }
        } else {
            stepEl.setAttribute('data-status', 'waiting');
            stepEl.querySelector('.step-icon').innerHTML = '<i class="fas fa-circle"></i>';
        }
    }

    document.getElementById('loginStatusMessage').textContent = message || '';

    if (status === 'done' || status === 'error') {
        document.getElementById('loginProgressClose').style.display = '';
        document.getElementById('loginProgressCancel').style.display = 'none';
    }
}

// Update Banner
let pendingUpdateUrl = null;

function showUpdateBanner(data) {
    pendingUpdateUrl = data.downloadUrl;
    document.getElementById('updateNewVersion').textContent = data.newVersion;
    document.getElementById('updateBanner').style.display = '';
    document.getElementById('updateProgressContainer').style.display = 'none';
    document.querySelector('.btn-update-install').disabled = false;
    document.querySelector('.btn-update-install').innerHTML = '<i class="fas fa-download"></i> Jetzt installieren';
}

function dismissUpdate() {
    document.getElementById('updateBanner').style.display = 'none';
}

async function startUpdate() {
    const btn = document.querySelector('.btn-update-install');
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-circle-notch fa-spin"></i> Wird heruntergeladen...';
    document.getElementById('updateProgressContainer').style.display = '';
    document.querySelector('.btn-update-dismiss').style.display = 'none';

    try {
        await sendMessageToCSharp('START_UPDATE', { downloadUrl: pendingUpdateUrl });
    } catch (error) {
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-download"></i> Erneut versuchen';
        document.querySelector('.btn-update-dismiss').style.display = '';
        showToast('Update fehlgeschlagen: ' + error.message, 'error');
    }
}

function updateProgress(data) {
    const fill = document.getElementById('updateProgressFill');
    const text = document.getElementById('updateProgressText');
    fill.style.width = data.percentage + '%';
    text.textContent = data.message || '';
}

function updateReady() {
    const btn = document.querySelector('.btn-update-install');
    btn.innerHTML = '<i class="fas fa-check-circle"></i> App wird neu gestartet...';
    document.getElementById('updateProgressFill').style.width = '100%';
    document.getElementById('updateProgressText').textContent = 'Installation wird vorbereitet...';
}

// Global message listener for push messages from C#
window.chrome.webview.addEventListener('message', (event) => {
    const data = event.data;
    if (!data || !data.type) return;

    switch (data.type) {
        case 'loginProgress':
            updateLoginProgress(data);
            break;
        case 'updateAvailable':
            showUpdateBanner(data);
            break;
        case 'updateProgress':
            updateProgress(data);
            break;
        case 'updateReady':
            updateReady();
            break;
        case 'toast':
            showToast(data.message, data.level || 'info');
            break;
    }
});


// OP.GG öffnen
function openOpGG(region, name, tag) {
    const url = `https://op.gg/lol/summoners/${region}/${name}-${tag}`;
    window.open(url, '_blank');
}

// Toast Notification anzeigen
function showToast(message, type = 'success') {
    toastMessage.textContent = message;
    toast.className = `toast show ${type}`;

    switch (type) {
        case 'success':
            toastIcon.className = 'fas fa-check-circle';
            break;
        case 'error':
            toastIcon.className = 'fas fa-times-circle';
            break;
        case 'warning':
            toastIcon.className = 'fas fa-exclamation-triangle';
            break;
        case 'info':
            toastIcon.className = 'fas fa-info-circle';
            break;
    }

    setTimeout(() => {
        toast.classList.remove('show');
    }, 3000);
}

// Modal öffnen
function openModal(editId = null) {
    currentEditId = editId;

    if (editId) {
        const account = accounts.find(acc => acc.id === editId);
        modalTitle.textContent = 'Account bearbeiten';
        inputName.value = account.name;
        inputTag.value = account.tag;
        inputUsername.value = account.username;
        inputPassword.value = account.password;
        inputRegion.value = account.region;
    } else {
        modalTitle.textContent = 'Neuer Account';
        inputName.value = '';
        inputTag.value = '';
        inputUsername.value = '';
        inputPassword.value = '';
        inputRegion.value = '';
    }

    accountModal.classList.add('show');
}

// Modal schließen
function closeModal() {
    accountModal.classList.remove('show');
    currentEditId = null;
}

// Account speichern
async function saveAccount() {
    const name = inputName.value.trim();
    const tag = inputTag.value.trim();
    const username = inputUsername.value.trim();
    const password = inputPassword.value.trim();
    const region = inputRegion.value;

    if (!name || !tag || !username || !password || !region) {
        showToast('Bitte alle Felder ausfüllen!', 'warning');
        return;
    }

    const accountData = { name, tag, username, password, region };

    if (currentEditId) {
        await updateAccount(currentEditId, accountData);
    } else {
        await createAccount(accountData);
    }

    closeModal();
}

// Passwort im Input anzeigen/verbergen
function togglePasswordInput() {
    const inputEye = document.getElementById('inputEye');
    if (inputPassword.type === 'password') {
        inputPassword.type = 'text';
        inputEye.className = 'fas fa-eye-slash';
    } else {
        inputPassword.type = 'password';
        inputEye.className = 'fas fa-eye';
    }
}

// Passwort in Liste anzeigen/verbergen
function togglePassword(id) {
    const account = accounts.find(acc => acc.id === id);
    const passText = document.getElementById(`pass-${id}`);
    const eyeIcon = document.getElementById(`eye-${id}`);

    if (passText.textContent === '••••••••') {
        passText.textContent = account.password;
        eyeIcon.className = 'fas fa-eye-slash';
    } else {
        passText.textContent = '••••••••';
        eyeIcon.className = 'fas fa-eye';
    }
}

// Passwort kopieren
function copyPassword(id) {
    const account = accounts.find(acc => acc.id === id);
    navigator.clipboard.writeText(account.password).then(() => {
        showToast('Passwort in Zwischenablage kopiert!', 'info');
    }).catch(() => {
        showToast('Fehler beim Kopieren!', 'error');
    });
}

function copyUsername(id) {
    const account = accounts.find(acc => acc.id === id);
    const usernameWithTag = `${account.username}#${account.tag}`;
    navigator.clipboard.writeText(usernameWithTag).then(() => {
        showToast('Name + Tag in Zwischenablage kopiert!', 'info');
    }).catch(() => {
        showToast('Fehler beim Kopieren!', 'error');
    });
}

function copyUsernameOnly(id) {
    const account = accounts.find(acc => acc.id === id);
    navigator.clipboard.writeText(account.username).then(() => {
        showToast('Name in Zwischenablage kopiert!', 'info');
    }).catch(() => {
        showToast('Fehler beim Kopieren!', 'error');
    });
}

// Suche
searchInput.addEventListener('input', (e) => {
    const searchTerm = e.target.value.toLowerCase();
    const filtered = accounts.filter(acc =>
        acc.name.toLowerCase().includes(searchTerm) ||
        acc.tag.toLowerCase().includes(searchTerm) ||
        acc.username.toLowerCase().includes(searchTerm) ||
        acc.region.toLowerCase().includes(searchTerm)
    );
    renderAccounts(filtered);
});

// Neuen Account Button
addBtn.addEventListener('click', () => {
    openModal();
});

// Account bearbeiten
function editAccount(id) {
    openModal(id);
}

// Löschen-Modal öffnen
function deleteAccount(id) {
    deleteId = id;
    deleteModal.classList.add('show');
}

// Löschen-Modal schließen
function closeDeleteModal() {
    deleteModal.classList.remove('show');
    deleteId = null;
}

// Löschen bestätigen
async function confirmDelete() {
    if (deleteId) {
        await deleteAccountById(deleteId);
        closeDeleteModal();
    }
}

// Modal schließen beim Klick außerhalb
window.onclick = function (event) {
    if (event.target === accountModal) {
        closeModal();
    }
    if (event.target === deleteModal) {
        closeDeleteModal();
    }
}

// Initial laden
// === Settings / Personalization ===
const ACCENT_PRESETS = [
    { name: 'purple', color: '#a855f7', color2: '#8b5cf6' },
    { name: 'blue',   color: '#3b82f6', color2: '#2563eb' },
    { name: 'cyan',   color: '#06b6d4', color2: '#0891b2' },
    { name: 'green',  color: '#10b981', color2: '#059669' },
    { name: 'amber',  color: '#f59e0b', color2: '#d97706' },
    { name: 'rose',   color: '#f43f5e', color2: '#e11d48' },
    { name: 'pink',   color: '#ec4899', color2: '#db2777' },
    { name: 'red',    color: '#ef4444', color2: '#dc2626' },
    { name: 'indigo', color: '#6366f1', color2: '#4f46e5' },
    { name: 'teal',   color: '#14b8a6', color2: '#0d9488' },
    { name: 'lime',   color: '#84cc16', color2: '#65a30d' },
    { name: 'orange', color: '#f97316', color2: '#ea580c' },
];

const DEFAULT_SETTINGS = { accent: 'purple', background: 'gradient', density: 'comfortable' };

function loadSettings() {
    try {
        const stored = localStorage.getItem('lam_settings');
        return stored ? { ...DEFAULT_SETTINGS, ...JSON.parse(stored) } : { ...DEFAULT_SETTINGS };
    } catch {
        return { ...DEFAULT_SETTINGS };
    }
}

function saveSettings(s) {
    localStorage.setItem('lam_settings', JSON.stringify(s));
}

function hexToRgb(hex) {
    const m = hex.replace('#', '').match(/.{2}/g);
    return m ? `${parseInt(m[0], 16)}, ${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}` : '168, 85, 247';
}

function applySettings(s) {
    const preset = ACCENT_PRESETS.find(p => p.name === s.accent) || ACCENT_PRESETS[0];
    const root = document.documentElement;
    const rgb = hexToRgb(preset.color);
    root.style.setProperty('--accent', preset.color);
    root.style.setProperty('--accent-2', preset.color2);
    root.style.setProperty('--accent-soft', `rgba(${rgb}, 0.15)`);
    root.style.setProperty('--accent-glow', `rgba(${rgb}, 0.35)`);
    root.setAttribute('data-bg', s.background);
    root.setAttribute('data-density', s.density);
}

function renderSettingsUI(s) {
    const grid = document.getElementById('colorGrid');
    grid.innerHTML = '';
    ACCENT_PRESETS.forEach(p => {
        const sw = document.createElement('div');
        sw.className = 'color-swatch' + (s.accent === p.name ? ' active' : '');
        sw.style.background = `linear-gradient(135deg, ${p.color}, ${p.color2})`;
        sw.style.color = p.color;
        sw.title = p.name;
        sw.onclick = () => { s.accent = p.name; applySettings(s); saveSettings(s); renderSettingsUI(s); };
        grid.appendChild(sw);
    });
    document.querySelectorAll('#bgGrid .option-card').forEach(c => {
        c.classList.toggle('active', c.dataset.bg === s.background);
        c.onclick = () => { s.background = c.dataset.bg; applySettings(s); saveSettings(s); renderSettingsUI(s); };
    });
    document.querySelectorAll('#densityGrid .option-card').forEach(c => {
        c.classList.toggle('active', c.dataset.density === s.density);
        c.onclick = () => { s.density = c.dataset.density; applySettings(s); saveSettings(s); renderSettingsUI(s); };
    });
}

let _settings = loadSettings();
applySettings(_settings);

function openSettings() {
    _settings = loadSettings();
    renderSettingsUI(_settings);
    document.getElementById('settingsModal').classList.add('show');
}

function closeSettings() {
    document.getElementById('settingsModal').classList.remove('show');
}

function resetSettings() {
    _settings = { ...DEFAULT_SETTINGS };
    saveSettings(_settings);
    applySettings(_settings);
    renderSettingsUI(_settings);
    showToast('Einstellungen zurückgesetzt', 'info');
}

// Show debug-only update test button if a debugger is attached
sendMessageToCSharp('IS_DEBUGGER_ATTACHED').then(res => {
    if (res && res.attached) {
        document.getElementById('debugUpdateBtn').style.display = '';
    }
}).catch(() => {});

function testUpdate() {
    sendMessageToCSharp('TEST_UPDATE')
        .then(() => showToast('Update-Test gestartet (siehe Banner)', 'info'))
        .catch(err => showToast('Test fehlgeschlagen: ' + err.message, 'error'));
}

loadAccounts();
