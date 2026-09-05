// Spotnet Remote Mobile PWA Controller
(function() {
  'use strict';

  let currentFilterId = 'all';
  let currentPage = 1;
  let currentSort = 'date_desc';
  let currentSearch = '';
  let isLoadingSpots = false;
  let activeDetailSpot = null;
  let pollTimer = null;
  let availableFilters = [];
  let isSyncInProgress = false;

  // DOM Elements
  const spotsGrid = document.getElementById('spotsGrid');
  const favoritesGrid = document.getElementById('favoritesGrid');
  const queueList = document.getElementById('queueList');
  const searchInput = document.getElementById('searchInput');
  const clearSearchBtn = document.getElementById('clearSearchBtn');
  const sortSelect = document.getElementById('sortSelect');
  const resultsCount = document.getElementById('resultsCount');
  const loadMoreBtn = document.getElementById('loadMoreBtn');
  const detailModal = document.getElementById('detailModal');
  const pairingModal = document.getElementById('pairingModal');
  const pinInput = document.getElementById('pinInput');
  const btnSubmitPairing = document.getElementById('btnSubmitPairing');
  const pairingError = document.getElementById('pairingError');
  const toastContainer = document.getElementById('toastContainer');
  const navQueueBadge = document.getElementById('navQueueBadge');
  const headerSpeed = document.getElementById('headerSpeed');
  const queueSpeed = document.getElementById('queueSpeed');
  const queueRemaining = document.getElementById('queueRemaining');
  const overallProgressFill = document.getElementById('overallProgressFill');
  const queueActiveCount = document.getElementById('queueActiveCount');
  const queuePercent = document.getElementById('queuePercent');
  const btnSyncSpots = document.getElementById('btnSyncSpots');
  const filterChipsContainer = document.getElementById('filterChipsContainer');
  const subFilterChipsContainer = document.getElementById('subFilterChipsContainer');
  const pageSizeSelect = document.getElementById('pageSizeSelect');
  const commentsList = document.getElementById('commentsList');
  const commentsCount = document.getElementById('commentsCount');
  const btnRefreshComments = document.getElementById('btnRefreshComments');
  const commentNicknameInput = document.getElementById('commentNicknameInput');
  const commentBodyInput = document.getElementById('commentBodyInput');
  const btnSubmitComment = document.getElementById('btnSubmitComment');
  const tabBtnPassword = document.getElementById('tabBtnPassword');
  const tabBtnPin = document.getElementById('tabBtnPin');
  const authViewPassword = document.getElementById('authViewPassword');
  const authViewPin = document.getElementById('authViewPin');

  const loginPassword = document.getElementById('loginPassword');
  const btnToggleLoginPassword = document.getElementById('btnToggleLoginPassword');
  const btnSubmitLogin = document.getElementById('btnSubmitLogin');
  const loginError = document.getElementById('loginError');
  const logoutBtn = document.getElementById('logoutBtn');
  const notifModal = document.getElementById('notifModal');
  const btnHeaderNotif = document.getElementById('btnHeaderNotif');
  const headerNotifBadge = document.getElementById('headerNotifBadge');
  const closeNotifBtn = document.getElementById('closeNotifBtn');
  const btnMarkAllRead = document.getElementById('btnMarkAllRead');
  const notifModalCount = document.getElementById('notifModalCount');
  const notifListContainer = document.getElementById('notifListContainer');

  function getPageSize() {
    const saved = parseInt(localStorage.getItem('spotnet_page_size'), 10);
    return [25, 50, 100, 200].includes(saved) ? saved : 50;
  }

  // Explicitly ensure modals are hidden on load
  if (detailModal) {
    detailModal.classList.remove('active');
    detailModal.style.display = 'none';
  }
  if (pairingModal) {
    pairingModal.classList.remove('active');
    pairingModal.style.display = 'none';
  }
  if (notifModal) {
    notifModal.classList.remove('active');
    notifModal.style.display = 'none';
  }

  // Token Management
  function getToken() {
    return localStorage.getItem('spotnet_device_token') || '';
  }

  function setToken(token) {
    if (token) {
      localStorage.setItem('spotnet_device_token', token);
    } else {
      localStorage.removeItem('spotnet_device_token');
    }
  }

  async function apiFetch(url, options = {}) {
    options.headers = options.headers || {};
    const token = getToken();
    if (token) {
      options.headers['Authorization'] = 'Bearer ' + token;
    }
    const response = await fetch(url, options);
    if (response.status === 401) {
      showPairingModal();
      throw new Error('Niet geautoriseerd');
    }
    return response;
  }

  // Toast Notification
  function showToast(message, actionText = null, actionCallback = null, durationMs = 2800) {
    const toast = document.createElement('div');
    toast.className = actionText ? 'toast toast-action' : 'toast';

    const textSpan = document.createElement('span');
    textSpan.textContent = message;
    toast.appendChild(textSpan);

    if (actionText && actionCallback) {
      const btn = document.createElement('button');
      btn.className = 'toast-action-btn';
      btn.type = 'button';
      btn.innerHTML = `${escapeHtml(actionText)} &rarr;`;
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        actionCallback();
        toast.remove();
      });
      toast.appendChild(btn);
    }

    toastContainer.appendChild(toast);
    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transition = 'opacity 0.3s';
      setTimeout(() => toast.remove(), 300);
    }, durationMs);
  }

  // Quality Badges Extraction
  function extractBadges(title) {
    const badges = [];
    const lower = title.toLowerCase();
    if (lower.includes('2160p') || lower.includes('4k') || lower.includes('uhd')) {
      badges.push({ text: '4K UHD', class: 'uhd' });
    } else if (lower.includes('1080p') || lower.includes('1080i') || lower.includes('fhd')) {
      badges.push({ text: '1080p', class: '' });
    } else if (lower.includes('720p') || lower.includes('hd')) {
      badges.push({ text: '720p', class: '' });
    }

    if (lower.includes('x265') || lower.includes('hevc') || lower.includes('h.265')) {
      badges.push({ text: 'x265', class: '' });
    } else if (lower.includes('x264') || lower.includes('h.264')) {
      badges.push({ text: 'x264', class: '' });
    }

    if (lower.includes('nl subs') || lower.includes('nl-subs') || lower.includes('dutch subs') || lower.includes('ondertiteld')) {
      badges.push({ text: 'NL Subs', class: 'subs' });
    } else if (lower.includes('nl gesproken') || lower.includes('dutch audio') || lower.includes('nederlands')) {
      badges.push({ text: 'NL Audio', class: 'subs' });
    }

    return badges;
  }

  function getCatIcon(cat) {
    const c = parseInt(cat, 10);
    switch (c) {
      case 1: return '🎬';
      case 6: return '📺';
      case 5: return '📚';
      case 2: return '🎵';
      case 3: return '🎮';
      case 4: return '💻';
      case 9: return '🔞';
      default: return '📦';
    }
  }

  // Render Spot Card
  function createSpotCard(spot) {
    const card = document.createElement('div');
    card.className = 'spot-card';
    card.dataset.id = spot.id;

    const badges = extractBadges(spot.title);
    const badgesHtml = badges.map(b => `<span class="quality-badge ${b.class}">${b.text}</span>`).join('');

    card.innerHTML = `
      <div class="spot-card-top">
        <div class="card-cat-badge">${getCatIcon(spot.category)}</div>
        <div class="card-header-info">
          <div class="spot-title" title="${escapeHtml(spot.title)}">${escapeHtml(spot.title)}</div>
          <div class="spot-sub-info">
            <span>${escapeHtml(spot.poster)}</span> &bull; <span>${spot.formattedDate}</span>
          </div>
          ${badgesHtml ? `<div class="badges-row">${badgesHtml}</div>` : ''}
        </div>
      </div>
      <div class="spot-card-bottom">
        <span class="file-meta-info">${spot.formattedSize}</span>
        <div class="card-actions">
          <button class="btn-card-action download" data-action="download" title="Download naar PC">
            <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="7 10 12 15 17 10"></polyline>
              <line x1="12" y1="15" x2="12" y2="3"></line>
            </svg>
            <span>Download</span>
          </button>
        </div>
      </div>
    `;

    // Click to view details
    card.addEventListener('click', (e) => {
      const downloadBtn = e.target.closest('[data-action="download"]');
      if (downloadBtn) {
        e.stopPropagation();
        triggerDownload(spot.id, spot.messageId, spot.title, downloadBtn);
        return;
      }
      openDetail(spot);
    });

    return card;
  }

  // Load Filters dynamically from Desktop Spotnet
  async function loadFilters() {
    try {
      const res = await apiFetch('/api/v1/filters');
      const filters = await res.json();
      availableFilters = filters || [];
      renderFilterChips();
    } catch (err) {
      console.error('Fout bij laden van filters:', err);
    }
  }

  function renderFilterChips() {
    if (!filterChipsContainer) return;
    filterChipsContainer.innerHTML = '';

    // Alles chip
    const allChip = document.createElement('button');
    allChip.className = 'chip' + (currentFilterId === 'all' ? ' active' : '');
    allChip.dataset.filterId = 'all';
    allChip.textContent = 'Alles';
    allChip.addEventListener('click', () => selectFilter('all', null));
    filterChipsContainer.appendChild(allChip);

    // Favorieten chip
    const favChip = document.createElement('button');
    favChip.className = 'chip' + (currentFilterId === 'fav' ? ' active' : '');
    favChip.dataset.filterId = 'fav';
    favChip.textContent = '★ Favorieten';
    favChip.addEventListener('click', () => selectFilter('fav', null));
    filterChipsContainer.appendChild(favChip);

    // Dynamic filters
    availableFilters.forEach(filter => {
      const chip = document.createElement('button');
      chip.className = 'chip' + (currentFilterId === filter.id ? ' active' : '');
      chip.dataset.filterId = filter.id;
      const icon = filter.icon ? `${filter.icon} ` : '';
      const title = filter.name || filter.title || '';
      chip.textContent = `${icon}${title}`;
      chip.addEventListener('click', () => selectFilter(filter.id, filter));
      filterChipsContainer.appendChild(chip);
    });
  }

  function selectFilter(filterId, filterObj) {
    currentFilterId = filterId;

    document.querySelectorAll('#filterChipsContainer .chip').forEach(c => {
      c.classList.toggle('active', c.dataset.filterId === filterId);
    });

    if (filterId === 'fav') {
      hideSubFilters();
      switchView('view-favorites');
      loadFavorites();
      return;
    }

    const subFilters = filterObj ? (filterObj.children || filterObj.subFilters || []) : [];
    if (subFilters.length > 0) {
      renderSubFilters(filterObj, subFilters);
    } else {
      hideSubFilters();
    }

    currentPage = 1;
    loadSpots(false);
  }

  function renderSubFilters(parentFilter, subFilters) {
    if (!subFilterChipsContainer) return;
    subFilterChipsContainer.innerHTML = '';

    const parentTitle = parentFilter.name || parentFilter.title || '';

    // "Alles in <Parent>" chip
    const allSubChip = document.createElement('button');
    allSubChip.className = 'chip' + (currentFilterId === parentFilter.id ? ' active' : '');
    allSubChip.dataset.filterId = parentFilter.id;
    allSubChip.textContent = `Alles (${parentTitle})`;
    allSubChip.addEventListener('click', () => {
      currentFilterId = parentFilter.id;
      updateSubChipActive(parentFilter.id);
      currentPage = 1;
      loadSpots(false);
    });
    subFilterChipsContainer.appendChild(allSubChip);

    subFilters.forEach(sub => {
      const subChip = document.createElement('button');
      subChip.className = 'chip' + (currentFilterId === sub.id ? ' active' : '');
      subChip.dataset.filterId = sub.id;
      subChip.textContent = sub.name || sub.title || '';
      subChip.addEventListener('click', () => {
        currentFilterId = sub.id;
        updateSubChipActive(sub.id);
        currentPage = 1;
        loadSpots(false);
      });
      subFilterChipsContainer.appendChild(subChip);
    });

    subFilterChipsContainer.style.display = 'flex';
  }

  function updateSubChipActive(activeId) {
    if (!subFilterChipsContainer) return;
    subFilterChipsContainer.querySelectorAll('.chip').forEach(c => {
      c.classList.toggle('active', c.dataset.filterId === activeId);
    });
  }

  function hideSubFilters() {
    if (subFilterChipsContainer) {
      subFilterChipsContainer.innerHTML = '';
      subFilterChipsContainer.style.display = 'none';
    }
  }

  // Load Spots
  async function loadSpots(append = false) {
    if (isLoadingSpots) return;
    isLoadingSpots = true;
    if (!append) {
      spotsGrid.innerHTML = '<div style="padding:40px; text-align:center; color:#94a3b8; grid-column:1/-1;">Spots laden...</div>';
      currentPage = 1;
    }

    try {
      const pageSize = getPageSize();
      let url = `/api/v1/spots?page=${currentPage}&pageSize=${pageSize}&sort=${currentSort}`;
      if (currentFilterId && currentFilterId !== 'all' && currentFilterId !== 'fav') {
        url += `&filterId=${encodeURIComponent(currentFilterId)}`;
      }
      if (currentSearch.trim()) {
        url += `&query=${encodeURIComponent(currentSearch.trim())}`;
      }

      const res = await apiFetch(url);
      const spots = await res.json();

      if (!append) spotsGrid.innerHTML = '';

      if (spots.length === 0 && !append) {
        spotsGrid.innerHTML = '<div style="padding:40px; text-align:center; color:#94a3b8; grid-column:1/-1;">Geen spots gevonden.</div>';
        resultsCount.textContent = '0 resultaten';
        loadMoreBtn.style.display = 'none';
      } else {
        spots.forEach(spot => {
          spotsGrid.appendChild(createSpotCard(spot));
        });
        resultsCount.textContent = `${spotsGrid.children.length} spots geladen`;
        loadMoreBtn.style.display = spots.length >= pageSize ? 'block' : 'none';
      }
    } catch (err) {
      if (err.message !== 'Niet geautoriseerd') {
        console.error(err);
        spotsGrid.innerHTML = '<div style="padding:40px; text-align:center; color:#ef4444; grid-column:1/-1;">Fout bij laden van spots.</div>';
      }
    } finally {
      isLoadingSpots = false;
    }
  }

  // Load Favorites
  async function loadFavorites() {
    favoritesGrid.innerHTML = '<div style="padding:40px; text-align:center; color:#94a3b8; grid-column:1/-1;">Favorieten laden...</div>';
    try {
      const res = await apiFetch('/api/v1/favorites?page=1&pageSize=50');
      const favs = await res.json();
      favoritesGrid.innerHTML = '';
      if (favs.length === 0) {
        favoritesGrid.innerHTML = '<div style="padding:40px; text-align:center; color:#94a3b8; grid-column:1/-1;">Nog geen favorieten opgeslagen.</div>';
      } else {
        favs.forEach(spot => {
          favoritesGrid.appendChild(createSpotCard(spot));
        });
      }
    } catch (err) {
      console.error(err);
    }
  }

  // Open Detail Modal
  async function openDetail(spot) {
    activeDetailSpot = spot;
    document.getElementById('detailTitle').textContent = spot.title;
    document.getElementById('detailCategory').textContent = spot.categoryName;
    document.getElementById('detailPosterHandle').textContent = spot.poster;
    document.getElementById('detailDate').textContent = spot.formattedDate;
    document.getElementById('detailSize').textContent = spot.formattedSize;
    document.getElementById('detailPosterCatIcon').textContent = getCatIcon(spot.category);

    const badges = extractBadges(spot.title);
    document.getElementById('detailBadges').innerHTML = badges.map(b => `<span class="quality-badge ${b.class}">${b.text}</span>`).join('');

    const posterImg = document.getElementById('detailPoster');
    const placeholder = document.getElementById('detailPosterPlaceholder');
    posterImg.style.display = 'none';
    placeholder.style.display = 'flex';

    // Load spot image with token & msgid query parameters for authorization
    const token = getToken();
    const tokenParam = token ? `&token=${encodeURIComponent(token)}` : '';
    const msgidParam = spot.messageId ? `&msgid=${encodeURIComponent(spot.messageId)}` : '';
    const imgUrl = `/api/v1/spots/${spot.id}/image?_t=${Date.now()}${tokenParam}${msgidParam}`;

    posterImg.onload = () => {
      posterImg.style.display = 'block';
      placeholder.style.display = 'none';
    };
    posterImg.onerror = () => {
      posterImg.style.display = 'none';
      placeholder.style.display = 'flex';
    };
    posterImg.src = imgUrl;

    document.getElementById('detailDescription').innerHTML = '<em>Omschrijving ophalen van Usenet...</em>';
    detailModal.classList.add('active');
    detailModal.style.display = 'flex';
    if (window.SpotnetNative && typeof window.SpotnetNative.setSwipeRefreshEnabled === 'function') {
      window.SpotnetNative.setSwipeRefreshEnabled(false);
    }

    // Load Comments for this spot
    loadComments(spot.id, spot.messageId);

    try {
      const res = await apiFetch(`/api/v1/spots/${spot.id}`);
      const detail = await res.json();
      activeDetailSpot = detail;
      document.getElementById('detailDescription').innerHTML = detail.description || 'Geen omschrijving';
      updateFavBtnState(detail.isFavorite);
    } catch (err) {
      console.error(err);
      document.getElementById('detailDescription').innerHTML = 'Kon omschrijving niet laden.';
    }
  }

  function updateFavBtnState(isFav) {
    const btn = document.getElementById('btnToggleFavorite');
    if (btn) btn.style.color = isFav ? '#f59e0b' : 'inherit';
  }

  // Load Comments
  async function loadComments(spotId, messageId) {
    if (!commentsList) return;
    commentsList.innerHTML = '<div class="comments-empty">Reacties laden...</div>';
    if (commentsCount) commentsCount.textContent = '...';

    try {
      const msgParam = messageId ? `?msgid=${encodeURIComponent(messageId)}` : '';
      const res = await apiFetch(`/api/v1/spots/${spotId}/comments${msgParam}`);
      const comments = await res.json();

      if (commentsCount) commentsCount.textContent = comments ? comments.length : 0;

      if (!comments || comments.length === 0) {
        commentsList.innerHTML = '<div class="comments-empty">Nog geen reacties geplaatst op deze spot. Wees de eerste!</div>';
        return;
      }

      commentsList.innerHTML = '';
      comments.forEach(comment => {
        const card = document.createElement('div');
        card.className = 'comment-card';

        const nick = comment.sender || comment.nickname || 'Anoniem';
        const dateStr = comment.dateFormatted || comment.formattedDate || '';
        const avatarUrl = comment.avatar || comment.avatarUrl || '';
        const initial = nick.charAt(0).toUpperCase();
        const avatarHtml = avatarUrl 
          ? `<img src="${avatarUrl}" alt="${escapeHtml(nick)}">`
          : `<span>${initial}</span>`;

        const verifiedBadge = (comment.isVerified || comment.isAuthor) ? `
          <span class="badge-verified" title="Geverifieerde Spotnet auteur">
            <svg viewBox="0 0 24 24" width="13" height="13" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg>
          </span>
        ` : '';

        const bodyContent = comment.bodyHtml || escapeHtml(comment.rawBody || comment.body || '');

        card.innerHTML = `
          <div class="comment-avatar">
            ${avatarHtml}
          </div>
          <div class="comment-main">
            <div class="comment-header">
              <span class="comment-author-badge">
                ${escapeHtml(nick)}
                ${verifiedBadge}
              </span>
              <span class="comment-date">${dateStr}</span>
            </div>
            <div class="comment-body">
              ${bodyContent}
            </div>
          </div>
        `;
        commentsList.appendChild(card);
      });
    } catch (err) {
      console.error('Fout bij ophalen reacties:', err);
      commentsList.innerHTML = '<div class="comments-empty">Kon reacties niet laden.</div>';
      if (commentsCount) commentsCount.textContent = '0';
    }
  }

  // Submit Comment
  async function submitComment() {
    if (!activeDetailSpot) return;

    const nickname = (commentNicknameInput ? commentNicknameInput.value : '').trim();
    const body = (commentBodyInput ? commentBodyInput.value : '').trim();

    if (!nickname) {
      showToast('Vul alsjeblieft je naam in.');
      if (commentNicknameInput) commentNicknameInput.focus();
      return;
    }
    if (!body) {
      showToast('Vul alsjeblieft een reactie in.');
      if (commentBodyInput) commentBodyInput.focus();
      return;
    }

    localStorage.setItem('spotnet_nickname', nickname);
    if (btnSubmitComment) {
      btnSubmitComment.disabled = true;
      btnSubmitComment.style.opacity = '0.6';
    }

    try {
      const res = await apiFetch(`/api/v1/spots/${activeDetailSpot.id}/comments`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          nickname: nickname,
          body: body,
          messageId: activeDetailSpot.messageId
        })
      });

      const result = await res.json();
      if (result.success) {
        showToast('✓ Reactie succesvol geplaatst!');
        if (commentBodyInput) commentBodyInput.value = '';
        loadComments(activeDetailSpot.id, activeDetailSpot.messageId);
      } else {
        showToast(result.errorMessage || 'Fout bij plaatsen van reactie.');
      }
    } catch (err) {
      console.error(err);
      showToast('Kon reactie niet versturen.');
    } finally {
      if (btnSubmitComment) {
        btnSubmitComment.disabled = false;
        btnSubmitComment.style.opacity = '1';
      }
    }
  }

  function insertTextAtCursor(textarea, text) {
    if (!textarea) return;
    const start = textarea.selectionStart || textarea.value.length;
    const end = textarea.selectionEnd || textarea.value.length;
    const val = textarea.value;
    textarea.value = val.substring(0, start) + text + val.substring(end);
    textarea.selectionStart = textarea.selectionEnd = start + text.length;
    textarea.focus();
  }

  // Trigger Sync ("Nieuwe spots ophalen")
  async function triggerSyncSpots() {
    if (isSyncInProgress) return;
    isSyncInProgress = true;

    if (btnSyncSpots) {
      btnSyncSpots.classList.add('syncing');
      btnSyncSpots.disabled = true;
    }

    showToast('Nieuwe spots ophalen gestart op PC...');

    try {
      await apiFetch('/api/v1/spots/sync', { method: 'POST' });

      // Poll status until sync completes
      const checkTimer = setInterval(async () => {
        try {
          const res = await apiFetch('/api/v1/status');
          const st = await res.json();
          if (!st.isSyncing) {
            clearInterval(checkTimer);
            isSyncInProgress = false;
            if (btnSyncSpots) {
              btnSyncSpots.classList.remove('syncing');
              btnSyncSpots.disabled = false;
            }
            showToast('✓ Spots bijwerken voltooid!');
            loadSpots(false);
            loadStatus();
          }
        } catch {
          clearInterval(checkTimer);
          isSyncInProgress = false;
          if (btnSyncSpots) {
            btnSyncSpots.classList.remove('syncing');
            btnSyncSpots.disabled = false;
          }
        }
      }, 2500);
    } catch (err) {
      console.error(err);
      isSyncInProgress = false;
      if (btnSyncSpots) {
        btnSyncSpots.classList.remove('syncing');
        btnSyncSpots.disabled = false;
      }
      showToast('Kon ophalen niet starten.');
    }
  }

  // Pull-to-refresh & Sync functionality
  async function triggerPullToRefresh() {
    // Disable refresh/sync when a spot is open or another modal is active
    if (detailModal && detailModal.classList.contains('active')) return;
    if (notifModal && notifModal.classList.contains('active')) return;

    const activeView = document.querySelector('.view.active')?.id || 'view-discover';
    try {
      if (activeView === 'view-queue') {
        await updateQueue();
        showToast('Downloads bijgewerkt');
      } else if (activeView === 'view-favorites') {
        await loadFavorites();
        showToast('Favorieten bijgewerkt');
      } else if (activeView === 'view-settings') {
        await loadStatus();
        showToast('Status bijgewerkt');
      } else {
        // Spots view: trigger sync on PC from Usenet and refresh current spots list
        triggerSyncSpots();
        await loadSpots(false);
      }
    } catch (err) {
      console.error('Pull to refresh failed:', err);
    } finally {
      if (window.SpotnetNative && typeof window.SpotnetNative.setRefreshing === 'function') {
        window.SpotnetNative.setRefreshing(false);
      }
    }
  }
  window.triggerPullToRefresh = triggerPullToRefresh;

  function setupPullToRefresh() {
    // If running inside Android native companion app, Android's SwipeRefreshLayout handles native touch gestures
    if (window.SpotnetNative && typeof window.SpotnetNative.isNativeApp === 'function' && window.SpotnetNative.isNativeApp()) {
      return;
    }

    const ptr = document.getElementById('pullToRefresh');
    if (!ptr) return;
    const ptrText = ptr.querySelector('.ptr-text');

    let startY = 0;
    let isPulling = false;
    let isRefreshing = false;
    const threshold = 55;
    const maxPull = 100;

    window.addEventListener('touchstart', (e) => {
      if (isRefreshing) return;
      // Disable pull-to-refresh when any spot is open or modal is active
      if (detailModal && detailModal.classList.contains('active')) return;
      if (notifModal && notifModal.classList.contains('active')) return;

      if (window.scrollY <= 0 && e.touches.length === 1) {
        startY = e.touches[0].clientY;
        isPulling = false;
      } else {
        startY = 0;
      }
    }, { passive: true });

    window.addEventListener('touchmove', (e) => {
      if (!startY || isRefreshing) return;
      if (detailModal && detailModal.classList.contains('active')) {
        startY = 0;
        return;
      }
      if (window.scrollY > 0) {
        startY = 0;
        if (isPulling) {
          isPulling = false;
          ptr.style.transform = '';
          ptr.style.opacity = '0';
          ptr.classList.remove('ptr-pulling', 'ptr-ready');
        }
        return;
      }

      const currentY = e.touches[0].clientY;
      const diff = currentY - startY;

      if (diff > 12) {
        isPulling = true;
        ptr.classList.add('ptr-pulling');
        const pullDist = Math.min(diff * 0.4, maxPull);
        const translateY = -85 + pullDist;
        const opacity = Math.min(1, pullDist / 35);
        ptr.style.transform = `translate(-50%, ${translateY}px)`;
        ptr.style.opacity = `${opacity}`;

        if (pullDist >= threshold) {
          ptr.classList.add('ptr-ready');
          if (ptrText) ptrText.textContent = 'Laat los om te synchroniseren';
        } else {
          ptr.classList.remove('ptr-ready');
          if (ptrText) ptrText.textContent = 'Trek omlaag om te vernieuwen';
        }
      }
    }, { passive: true });

    window.addEventListener('touchend', async () => {
      if (!isPulling || isRefreshing) return;
      isPulling = false;
      startY = 0;
      ptr.classList.remove('ptr-pulling');

      const isReady = ptr.classList.contains('ptr-ready');
      ptr.classList.remove('ptr-ready');

      if (isReady) {
        isRefreshing = true;
        ptr.classList.add('ptr-loading');
        ptr.style.transform = 'translate(-50%, 0px)';
        ptr.style.opacity = '1';
        if (ptrText) ptrText.textContent = 'Spots bijwerken...';

        try {
          await triggerPullToRefresh();
        } finally {
          setTimeout(() => {
            ptr.classList.remove('ptr-loading');
            ptr.style.transform = '';
            ptr.style.opacity = '0';
            isRefreshing = false;
          }, 450);
        }
      } else {
        ptr.style.transform = '';
        ptr.style.opacity = '0';
      }
    }, { passive: true });
  }

  // Trigger Download
  async function triggerDownload(spotId, messageId, title, triggerBtn = null) {
    try {
      if (triggerBtn) {
        triggerBtn.dataset.originalHtml = triggerBtn.innerHTML;
        triggerBtn.disabled = true;
      }

      showToast(`Download aanvragen voor "${title.substring(0, 30)}..."`);
      const res = await apiFetch(`/api/v1/spots/${spotId}/download`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ messageId: messageId })
      });
      const data = await res.json();
      if (data.success) {
        showToast('✓ Toegevoegd aan downloadwachtrij!', 'Ga naar downloads', () => {
          closeDetailModal();
          switchView('view-queue');
        }, 5000);

        if (triggerBtn) {
          triggerBtn.disabled = false;
          triggerBtn.classList.add('btn-download-success');
          triggerBtn.innerHTML = `
            <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5">
              <polyline points="20 6 9 17 4 12"></polyline>
            </svg>
            <span>Ga naar downloads</span>
          `;
          const navHandler = (e) => {
            e.stopPropagation();
            closeDetailModal();
            switchView('view-queue');
          };
          triggerBtn.addEventListener('click', navHandler, { once: true });

          setTimeout(() => {
            triggerBtn.removeEventListener('click', navHandler);
            triggerBtn.classList.remove('btn-download-success');
            if (triggerBtn.dataset.originalHtml) {
              triggerBtn.innerHTML = triggerBtn.dataset.originalHtml;
            }
          }, 5000);
        }

        updateQueue();
      } else {
        if (triggerBtn) triggerBtn.disabled = false;
        showToast('Fout bij toevoegen aan wachtrij.');
      }
    } catch (err) {
      console.error(err);
      if (triggerBtn) triggerBtn.disabled = false;
      showToast('Kon download niet starten.');
    }
  }

  // Queue Management
  let lastActiveQueueCount = null;
  async function updateQueue() {
    try {
      const res = await apiFetch('/api/v1/queue');
      const q = await res.json();

      headerSpeed.textContent = q.overallSpeedFormatted || '0 KB/s';
      queueSpeed.textContent = q.overallSpeedFormatted || '0 MB/s';
      queueRemaining.textContent = q.remainingSizeFormatted || '0 MB';
      overallProgressFill.style.width = `${Math.min(100, Math.max(0, q.overallProgress))}%`;
      queuePercent.textContent = `${Math.round(q.overallProgress)}%`;
      queueActiveCount.textContent = `${q.activeCount} actieve download${q.activeCount === 1 ? '' : 's'}`;

      // Check if a download just finished to immediately pop notification
      if (lastActiveQueueCount !== null && q.activeCount < lastActiveQueueCount) {
        fetchNotifications(true);
      }
      lastActiveQueueCount = q.activeCount;

      if (q.activeCount > 0) {
        navQueueBadge.textContent = q.activeCount;
        navQueueBadge.style.display = 'inline-block';
      } else {
        navQueueBadge.style.display = 'none';
      }

      if (document.getElementById('view-queue').classList.contains('active')) {
        renderQueueList(q.items);
      }
    } catch (err) {
      // ignore when not paired
    }
  }

  function renderQueueList(items) {
    if (!items || items.length === 0) {
      queueList.innerHTML = '<div class="empty-state"><p>De downloadlijst is momenteel leeg.</p></div>';
      return;
    }

    // Sort downloads so the most recent / newest are at top, older at bottom
    const sortedItems = [...items].sort((a, b) => {
      const idA = parseInt(a.id, 10) || 0;
      const idB = parseInt(b.id, 10) || 0;
      if (idA !== idB) return idB - idA;
      return 0;
    });

    queueList.innerHTML = '';
    sortedItems.forEach(item => {
      const isComplete = item.isComplete || item.progress >= 100 || item.status === 'Voltooid' || item.status === 'Compleet';
      const isPaused = !isComplete && item.isPaused;
      const card = document.createElement('div');
      card.className = 'queue-item-card';

      let statusBadgeClass = 'queue-status-badge';
      if (isComplete) statusBadgeClass += ' badge-completed';
      else if (isPaused) statusBadgeClass += ' badge-paused';

      let fillClass = 'queue-progress-fill';
      if (isComplete) fillClass += ' fill-completed';
      else if (isPaused) fillClass += ' fill-paused';

      const progressPct = isComplete ? 100 : Math.min(100, Math.max(0, item.progress));
      const statusText = isComplete ? 'Voltooid' : escapeHtml(item.status);
      const metaText = isComplete
        ? `Voltooid • ${item.totalSizeFormatted || ''}`
        : `${progressPct.toFixed(1)}% • ${item.speedFormatted || '0 KB/s'} • ${item.totalSizeFormatted || ''}`;

      const actionButtonsHtml = isComplete ? `
        <button class="btn-icon" data-action="cancel" data-id="${item.id}" title="Verwijderen uit lijst">
          🗑
        </button>
      ` : `
        <button class="btn-icon" data-action="${isPaused ? 'resume' : 'pause'}" data-id="${item.id}" title="${isPaused ? 'Hervatten' : 'Pauzeren'}">
          ${isPaused ? '▶' : '⏸'}
        </button>
        <button class="btn-icon" data-action="cancel" data-id="${item.id}" title="Annuleren">
          🗑
        </button>
      `;

      card.innerHTML = `
        <div class="queue-item-header">
          <span class="queue-item-title">${escapeHtml(item.title)}</span>
          <span class="${statusBadgeClass}">${statusText}</span>
        </div>
        <div class="queue-progress-bar">
          <div class="${fillClass}" style="width: ${progressPct}%"></div>
        </div>
        <div class="queue-item-footer">
          <span>${metaText}</span>
          <div class="queue-actions">
            ${actionButtonsHtml}
          </div>
        </div>
      `;

      card.querySelectorAll('.btn-icon').forEach(btn => {
        btn.addEventListener('click', async (e) => {
          e.stopPropagation();
          const action = btn.dataset.action;
          const id = btn.dataset.id;
          if (action === 'pause') {
            btn.disabled = true;
            try {
              await apiFetch(`/api/v1/queue/${id}/pause`, { method: 'POST' });
              showToast('Download gepauzeerd');
            } catch (err) {
              console.error(err);
            }
          } else if (action === 'resume') {
            btn.disabled = true;
            try {
              await apiFetch(`/api/v1/queue/${id}/resume`, { method: 'POST' });
              showToast('Download hervat');
            } catch (err) {
              console.error(err);
            }
          } else if (action === 'cancel') {
            btn.disabled = true;
            btn.style.opacity = '0.4';
            try {
              const res = await apiFetch(`/api/v1/queue/${id}`, { method: 'DELETE' });
              const data = await res.json();
              if (data && data.success) {
                showToast(isComplete ? 'Download verwijderd uit lijst' : 'Download geannuleerd en verwijderd');
              } else {
                showToast('Kon download niet verwijderen');
              }
            } catch (err) {
              console.error('Delete error:', err);
              showToast('Fout bij verwijderen van download');
            }
          }
          updateQueue();
        });
      });

      queueList.appendChild(card);
    });
  }

  // Load Status / Info
  async function loadStatus() {
    try {
      const res = await apiFetch('/api/v1/status');
      const st = await res.json();
      document.getElementById('infoVersion').textContent = st.version;
      document.getElementById('infoProvider').textContent = st.currentProvider || 'Actief';
      document.getElementById('infoSpotsCount').textContent = st.totalSpotsInDb ? Number(st.totalSpotsInDb).toLocaleString('nl-NL') : '-';
      document.getElementById('infoPort').textContent = st.port;
      document.getElementById('statusText').textContent = st.isSyncing ? 'Spots bijwerken op PC...' : 'Verbonden met PC';
      document.querySelector('.status-dot').className = 'status-dot online';

      if (btnSyncSpots) {
        if (st.isSyncing) {
          btnSyncSpots.classList.add('syncing');
          btnSyncSpots.disabled = true;
        } else if (!isSyncInProgress) {
          btnSyncSpots.classList.remove('syncing');
          btnSyncSpots.disabled = false;
        }
      }

      if (st.defaultNickname && !localStorage.getItem('spotnet_nickname') && commentNicknameInput && !commentNicknameInput.value) {
        commentNicknameInput.value = st.defaultNickname;
      }
    } catch {
      document.getElementById('statusText').textContent = 'Offline';
      document.querySelector('.status-dot').className = 'status-dot';
    }
  }

  // Pairing Flow
  function showPairingModal() {
    pairingModal.classList.add('active');
    pairingModal.style.display = 'flex';
  }

  async function submitLogin() {
    if (!loginPassword) return;

    const password = loginPassword.value;

    if (!password) {
      if (loginError) {
        loginError.textContent = 'Vul je wachtwoord in.';
        loginError.style.display = 'block';
      }
      return;
    }

    if (loginError) loginError.style.display = 'none';
    if (btnSubmitLogin) {
      btnSubmitLogin.disabled = true;
      btnSubmitLogin.textContent = 'Inloggen...';
    }

    try {
      const res = await fetch('/api/v1/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({

          password: password,
          deviceName: navigator.userAgent.includes('iPhone') ? 'iPhone' : (navigator.userAgent.includes('Android') ? 'Android' : 'Mobiele Browser')
        })
      });

      const data = await res.json();
      if (data.success && data.deviceToken) {
        setToken(data.deviceToken);
        pairingModal.classList.remove('active');
        pairingModal.style.display = 'none';
        showToast('✓ Succesvol ingelogd bij Spotnet!');
        loginPassword.value = '';
        initApp();
      } else {
        if (loginError) {
          loginError.textContent = data.errorMessage || 'Onjuist wachtwoord.';
          loginError.style.display = 'block';
        }
      }
    } catch {
      if (loginError) {
        loginError.textContent = 'Kon geen verbinding maken met de server.';
        loginError.style.display = 'block';
      }
    } finally {
      if (btnSubmitLogin) {
        btnSubmitLogin.disabled = false;
        btnSubmitLogin.textContent = 'Inloggen';
      }
    }
  }

  async function submitPairing(pin, token = '') {
    pairingError.style.display = 'none';
    try {
      const res = await fetch('/api/v1/auth/pair', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          pin: pin,
          token: token,
          deviceName: navigator.userAgent.includes('iPhone') ? 'iPhone' : (navigator.userAgent.includes('Android') ? 'Android Telefoon' : 'Mobiele Browser')
        })
      });

      const data = await res.json();
      if (data.success && data.deviceToken) {
        setToken(data.deviceToken);
        pairingModal.classList.remove('active');
        pairingModal.style.display = 'none';
        showToast('✓ Succesvol gekoppeld met Spotnet!');
        initApp();
      } else {
        pairingError.textContent = data.errorMessage || 'Koppelcode onjuist of verlopen.';
        pairingError.style.display = 'block';
      }
    } catch {
      pairingError.textContent = 'Kon geen verbinding maken met de server.';
      pairingError.style.display = 'block';
    }
  }

  // Event Listeners for Authentication
  if (btnSubmitLogin) {
    btnSubmitLogin.addEventListener('click', submitLogin);
  }

  if (loginPassword) {
    loginPassword.addEventListener('keypress', (e) => {
      if (e.key === 'Enter') submitLogin();
    });
  }

  if (btnToggleLoginPassword && loginPassword) {
    btnToggleLoginPassword.addEventListener('click', () => {
      const isPwd = loginPassword.type === 'password';
      loginPassword.type = isPwd ? 'text' : 'password';
      btnToggleLoginPassword.style.color = isPwd ? 'var(--accent)' : 'var(--text-muted)';
    });
  }

  if (tabBtnPassword && tabBtnPin) {
    tabBtnPassword.addEventListener('click', () => {
      tabBtnPassword.classList.add('active');
      tabBtnPin.classList.remove('active');
      if (authViewPassword) authViewPassword.style.display = 'block';
      if (authViewPin) authViewPin.style.display = 'none';
      if (loginError) loginError.style.display = 'none';
    });

    tabBtnPin.addEventListener('click', () => {
      tabBtnPin.classList.add('active');
      tabBtnPassword.classList.remove('active');
      if (authViewPin) authViewPin.style.display = 'block';
      if (authViewPassword) authViewPassword.style.display = 'none';
      if (pairingError) pairingError.style.display = 'none';
    });
  }

  if (logoutBtn) {
    logoutBtn.addEventListener('click', () => {
      if (confirm('Weet je zeker dat je wilt uitloggen en dit toestel wilt ontkoppelen?')) {
        setToken('');
        showToast('Je bent uitgelogd.');
        if (window.SpotnetNative && typeof window.SpotnetNative.disconnect === 'function') {
          window.SpotnetNative.disconnect();
          return;
        }
        showPairingModal();
      }
    });
  }

  btnSubmitPairing.addEventListener('click', () => {
    submitPairing(pinInput.value.trim());
  });

  pinInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') submitPairing(pinInput.value.trim());
  });

  // Search input with debounce
  let searchTimeout = null;
  searchInput.addEventListener('input', () => {
    clearSearchBtn.style.display = searchInput.value ? 'block' : 'none';
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
      currentSearch = searchInput.value;
      loadSpots(false);
    }, 350);
  });

  clearSearchBtn.addEventListener('click', () => {
    searchInput.value = '';
    clearSearchBtn.style.display = 'none';
    currentSearch = '';
    loadSpots(false);
  });

  sortSelect.addEventListener('change', () => {
    currentSort = sortSelect.value;
    loadSpots(false);
  });

  loadMoreBtn.addEventListener('click', () => {
    currentPage++;
    loadSpots(true);
  });

  if (pageSizeSelect) {
    pageSizeSelect.value = String(getPageSize());
    pageSizeSelect.addEventListener('change', (e) => {
      localStorage.setItem('spotnet_page_size', e.target.value);
      currentPage = 1;
      loadSpots(false);
    });
  }

  if (btnSyncSpots) {
    btnSyncSpots.addEventListener('click', triggerSyncSpots);
  }

  if (btnRefreshComments) {
    btnRefreshComments.addEventListener('click', () => {
      if (activeDetailSpot) {
        loadComments(activeDetailSpot.id, activeDetailSpot.messageId);
      }
    });
  }

  if (btnSubmitComment) {
    btnSubmitComment.addEventListener('click', submitComment);
  }

  document.querySelectorAll('.emoji-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const emoji = btn.dataset.emoji;
      insertTextAtCursor(commentBodyInput, emoji);
    });
  });

  // Pre-fill nickname
  const savedNick = localStorage.getItem('spotnet_nickname');
  if (savedNick && commentNicknameInput) {
    commentNicknameInput.value = savedNick;
  }

  // Modal Close
  function closeDetailModal() {
    detailModal.classList.remove('active');
    detailModal.style.display = 'none';
    activeDetailSpot = null;
    if (window.SpotnetNative && typeof window.SpotnetNative.setSwipeRefreshEnabled === 'function') {
      window.SpotnetNative.setSwipeRefreshEnabled(true);
    }
  }

  document.getElementById('closeDetailBtn').addEventListener('click', closeDetailModal);
  detailModal.addEventListener('click', (e) => {
    if (e.target === detailModal) closeDetailModal();
  });

  document.getElementById('btnDownloadSpot').addEventListener('click', (e) => {
    if (activeDetailSpot) {
      triggerDownload(activeDetailSpot.id, activeDetailSpot.messageId, activeDetailSpot.title, e.currentTarget);
    }
  });

  document.getElementById('btnToggleFavorite').addEventListener('click', async () => {
    if (!activeDetailSpot) return;
    const newFav = !activeDetailSpot.isFavorite;
    activeDetailSpot.isFavorite = newFav;
    updateFavBtnState(newFav);
    try {
      if (newFav) {
        await apiFetch(`/api/v1/favorites/${activeDetailSpot.messageId}`, { method: 'POST' });
        showToast('Toegevoegd aan favorieten');
      } else {
        await apiFetch(`/api/v1/favorites/${activeDetailSpot.messageId}`, { method: 'DELETE' });
        showToast('Verwijderd uit favorieten');
      }
    } catch (err) {
      console.error(err);
    }
  });

  // Navigation
  function switchView(targetId) {
    document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
    document.getElementById(targetId).classList.add('active');
    const activeNav = document.querySelector(`.nav-btn[data-target="${targetId}"]`);
    if (activeNav) activeNav.classList.add('active');

    if (targetId === 'view-queue') updateQueue();
    if (targetId === 'view-favorites') loadFavorites();
    if (targetId === 'view-settings') loadStatus();
  }

  document.querySelectorAll('.nav-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      switchView(btn.dataset.target);
    });
  });

  document.getElementById('refreshQueueBtn').addEventListener('click', updateQueue);

  document.getElementById('logoutBtn').addEventListener('click', () => {
    if (confirm('Weet je zeker dat je dit apparaat wilt ontkoppelen?')) {
      setToken('');
      location.reload();
    }
  });

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  // --- Notifications Support ---
  let lastKnownUnreadCount = 0;
  let cachedNotifications = [];

  function requestNotificationPermission() {
    if ('Notification' in window && Notification.permission === 'default') {
      try {
        Notification.requestPermission();
      } catch (e) {}
    }
  }

  async function fetchNotifications(notifyUser = true) {
    try {
      const res = await apiFetch('/api/v1/notifications');
      if (!res.ok) return;
      const data = await res.json();
      updateNotificationUi(data.unreadCount, data.notifications);

      if (notifyUser && data.unreadCount > lastKnownUnreadCount) {
        const newItems = (data.notifications || []).filter(n => !n.isRead);
        if (newItems.length > 0) {
          const latest = newItems[0];
          // Android Native Companion App Push Notification
          if (window.SpotnetNative && typeof window.SpotnetNative.showNativeNotification === 'function') {
            window.SpotnetNative.showNativeNotification(
              latest.title || 'Spotnet Melding',
              latest.body || (latest.spotCount ? `${latest.spotCount} nieuwe spot(s)` : 'Download gereed'),
              (latest.spots && latest.spots[0]) ? latest.spots[0].id : 0
            );
          } else if ('Notification' in window && Notification.permission === 'granted') {
            try {
              new Notification(latest.title || 'Nieuwe Spotnet Melding', {
                body: latest.body || `${latest.spotCount} nieuwe spot(s)`,
                icon: '/icon.svg',
                badge: '/icon.svg'
              });
            } catch (e) {}
          }
        }
      }
      lastKnownUnreadCount = data.unreadCount || 0;
    } catch (err) {
      // Quiet fail if network glitch
    }
  }

  function updateNotificationUi(unreadCount, notifications) {
    cachedNotifications = notifications || [];

    if (unreadCount > 0) {
      if (headerNotifBadge) {
        headerNotifBadge.textContent = unreadCount > 99 ? '99+' : unreadCount;
        headerNotifBadge.style.display = 'block';
      }
      if (btnHeaderNotif) btnHeaderNotif.classList.add('has-unread');
      if (notifModalCount) {
        notifModalCount.textContent = `${unreadCount} ongelezen`;
        notifModalCount.style.display = 'inline-block';
      }
    } else {
      if (headerNotifBadge) headerNotifBadge.style.display = 'none';
      if (btnHeaderNotif) btnHeaderNotif.classList.remove('has-unread');
      if (notifModalCount) notifModalCount.style.display = 'none';
    }

    renderNotificationsList(cachedNotifications);
  }

  function renderNotificationsList(notifications) {
    if (!notifListContainer) return;

    if (!notifications || notifications.length === 0) {
      notifListContainer.innerHTML = '<div class="notif-empty">Geen meldingen gevonden</div>';
      return;
    }

    notifListContainer.innerHTML = notifications.map(notif => {
      const unreadClass = notif.isRead ? '' : 'unread';
      const spotsHtml = (notif.spots || []).map(s => `
        <div class="notif-spot-chip" data-spot-id="${s.id}" data-msg-id="${escapeHtml(s.messageId)}">
          <span class="notif-spot-name">${escapeHtml(s.title)}</span>
          <span class="notif-spot-meta">${escapeHtml(s.categoryName || '')} &bull; ${escapeHtml(s.formattedSize || '')}</span>
        </div>
      `).join('');

      return `
        <div class="notif-card ${unreadClass}" data-notif-id="${notif.id}">
          <div class="notif-card-header">
            <span class="notif-rule-badge">${escapeHtml(notif.ruleName || notif.ruleType || 'Melding')}</span>
            <span class="notif-time">${escapeHtml(notif.timeAgo || '')}</span>
          </div>
          <div class="notif-title">${escapeHtml(notif.title)}</div>
          <div class="notif-body">${escapeHtml(notif.body)}</div>
          ${spotsHtml ? `<div class="notif-spots-list">${spotsHtml}</div>` : ''}
        </div>
      `;
    }).join('');

    notifListContainer.querySelectorAll('.notif-spot-chip').forEach(chip => {
      chip.addEventListener('click', (e) => {
        e.stopPropagation();
        const spotId = chip.dataset.spotId;
        const notifCard = chip.closest('.notif-card');
        if (notifCard) {
          markNotificationRead(notifCard.dataset.notifId);
        }
        closeNotifModal();

        // Find spot in cached notifications to pass to openDetail
        let foundSpot = null;
        for (const n of cachedNotifications) {
          if (n.spots) {
            foundSpot = n.spots.find(s => s.id.toString() === spotId.toString());
            if (foundSpot) break;
          }
        }
        if (foundSpot) {
          openDetail(foundSpot);
        }
      });
    });

    notifListContainer.querySelectorAll('.notif-card').forEach(card => {
      card.addEventListener('click', () => {
        markNotificationRead(card.dataset.notifId);
      });
    });
  }

  function openNotifModal() {
    requestNotificationPermission();
    if (notifModal) {
      notifModal.style.display = 'flex';
      notifModal.classList.add('active');
    }
    if (window.SpotnetNative && typeof window.SpotnetNative.setSwipeRefreshEnabled === 'function') {
      window.SpotnetNative.setSwipeRefreshEnabled(false);
    }
    fetchNotifications(false);
  }

  function closeNotifModal() {
    if (notifModal) {
      notifModal.classList.remove('active');
      notifModal.style.display = 'none';
    }
    if (window.SpotnetNative && typeof window.SpotnetNative.setSwipeRefreshEnabled === 'function') {
      window.SpotnetNative.setSwipeRefreshEnabled(true);
    }
  }

  async function markNotificationRead(id) {
    if (!id) return;
    try {
      await apiFetch(`/api/v1/notifications/${id}/read`, { method: 'POST' });
      fetchNotifications(false);
    } catch (err) {
      console.error(err);
    }
  }

  async function markAllNotificationsRead() {
    try {
      await apiFetch('/api/v1/notifications/read-all', { method: 'POST' });
      showToast('Alle meldingen als gelezen gemarkeerd');
      fetchNotifications(false);
    } catch (err) {
      console.error(err);
    }
  }

  if (btnHeaderNotif) {
    btnHeaderNotif.addEventListener('click', openNotifModal);
  }
  if (closeNotifBtn) {
    closeNotifBtn.addEventListener('click', closeNotifModal);
  }
  if (notifModal) {
    notifModal.addEventListener('click', (e) => {
      if (e.target === notifModal) closeNotifModal();
    });
  }
  if (btnMarkAllRead) {
    btnMarkAllRead.addEventListener('click', markAllNotificationsRead);
  }

  // Expose global methods for native Android companion app
  window.openNotifModal = openNotifModal;
  window.closeNotifModal = closeNotifModal;

  function setupNativeIntegration() {
    if (!window.SpotnetNative) return;
    const nativeGroup = document.getElementById('nativeSettingsGroup');
    if (nativeGroup) nativeGroup.style.display = 'block';

    try {
      if (typeof window.SpotnetNative.getNotificationSettings === 'function') {
        const settings = JSON.parse(window.SpotnetNative.getNotificationSettings());
        const switchNotifs = document.getElementById('nativeSwitchNotifs');
        const switchSound = document.getElementById('nativeSwitchSound');
        const switchVibrate = document.getElementById('nativeSwitchVibrate');

        if (switchNotifs) {
          switchNotifs.checked = !!settings.notificationsEnabled;
          switchNotifs.onchange = () => {
            window.SpotnetNative.setNotificationSetting('notificationsEnabled', switchNotifs.checked);
          };
        }
        if (switchSound) {
          switchSound.checked = !!settings.soundEnabled;
          switchSound.onchange = () => {
            window.SpotnetNative.setNotificationSetting('soundEnabled', switchSound.checked);
          };
        }
        if (switchVibrate) {
          switchVibrate.checked = !!settings.vibrationEnabled;
          switchVibrate.onchange = () => {
            window.SpotnetNative.setNotificationSetting('vibrationEnabled', switchVibrate.checked);
          };
        }
      }
    } catch (e) {
      console.warn('Native settings setup failed:', e);
    }

    const btnTest = document.getElementById('btnNativeTestNotif');
    if (btnTest) {
      btnTest.onclick = () => {
        if (typeof window.SpotnetNative.triggerTestNotification === 'function') {
          window.SpotnetNative.triggerTestNotification();
        }
      };
    }
  }

  // App Initialization
  let pollCounter = 0;
  function initApp() {
    loadFilters();
    loadSpots(false);
    updateQueue();
    loadStatus();
    fetchNotifications(false);
    setupNativeIntegration();
    setupPullToRefresh();

    if (pollTimer) clearInterval(pollTimer);
    pollTimer = setInterval(() => {
      updateQueue();
      loadStatus();
      pollCounter++;
      if (pollCounter % 4 === 0) {
        fetchNotifications(true);
      }
    }, 2500);
  }

  // Check URL params for pairToken and authenticate silently without prompt
  async function checkQrPairingAndStart() {
    const urlParams = new URLSearchParams(window.location.search);
    const qrPairToken = urlParams.get('pairToken') || urlParams.get('token');

    if (qrPairToken) {
      // Bypassing login modal completely for QR scan
      if (pairingModal) {
        pairingModal.classList.remove('active');
        pairingModal.style.display = 'none';
      }
      if (notifModal) {
        notifModal.classList.remove('active');
        notifModal.style.display = 'none';
      }
      try {
        await submitPairing('', qrPairToken);
      } catch (e) {
        console.error('QR pairing failed:', e);
      }
      try {
        window.history.replaceState({}, document.title, window.location.pathname);
      } catch {}
      return;
    }

    if (!getToken()) {
      showPairingModal();
    } else {
      initApp();
    }
  }

  checkQrPairingAndStart();

  // Service Worker Registration for PWA
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker.register('/sw.js').catch(() => {});
    });
  }

})();
