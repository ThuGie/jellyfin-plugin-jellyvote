(function () {
  'use strict'

  var PLUGIN = 'JellyVote'
  var cfg = null
  var pollTimer = null

  function log () {
    if (window.console && console.debug) {
      console.debug.apply(console, ['[' + PLUGIN + ']'].concat([].slice.call(arguments)))
    }
  }

  function api (method, path, body) {
    var url = ApiClient.getUrl('JellyVote/' + path)
    var opts = { type: method, url: url, dataType: 'json', headers: {} }
    if (body !== undefined) {
      opts.data = JSON.stringify(body)
      opts.contentType = 'application/json'
    }
    return ApiClient.ajax(opts)
  }

  function ensureStyles () {
    if (document.getElementById('jellyvote-client-css')) return
    var style = document.createElement('style')
    style.id = 'jellyvote-client-css'
    style.textContent = [
      '.jv-fab-inbox{position:fixed;right:18px;bottom:18px;z-index:9998;border:0;border-radius:999px;',
      'background:var(--primary-accent-color,#00a4dc);color:#fff;padding:10px 14px;cursor:pointer;',
      'box-shadow:0 6px 18px rgba(0,0,0,.35);font-weight:600}',
      '.jv-fab-inbox .jv-badge{margin-left:6px;background:#111;border-radius:999px;padding:1px 7px;font-size:12px}',
      '.jv-modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.55);z-index:10000;display:flex;',
      'align-items:center;justify-content:center;padding:16px}',
      '.jv-modal{background:var(--dialog-background-color,#1c1c1c);color:inherit;border-radius:12px;',
      'max-width:420px;width:100%;padding:18px 18px 14px;box-shadow:0 12px 40px rgba(0,0,0,.45)}',
      '.jv-modal h2{margin:0 0 8px;font-size:1.15rem}',
      '.jv-modal p{margin:0 0 10px;opacity:.8;font-size:.9rem}',
      '.jv-modal label{display:block;margin:8px 0 4px;font-size:.85rem;opacity:.85}',
      '.jv-modal select,.jv-modal textarea,.jv-modal input{width:100%;box-sizing:border-box;margin-bottom:8px}',
      '.jv-modal .jv-row{display:flex;gap:8px;justify-content:flex-end;margin-top:12px;flex-wrap:wrap}',
      '.jv-modal button{cursor:pointer}',
      '.jv-item-btn{margin-left:8px}',
      '.jv-toast{position:fixed;left:50%;transform:translateX(-50%);bottom:80px;z-index:10001;',
      'background:rgba(20,20,20,.92);color:#fff;padding:10px 16px;border-radius:8px;max-width:90vw}'
    ].join('')
    document.head.appendChild(style)
  }

  function toast (msg) {
    var el = document.createElement('div')
    el.className = 'jv-toast'
    el.textContent = msg
    document.body.appendChild(el)
    setTimeout(function () { el.remove() }, 3500)
  }

  function getItemIdFromLocation () {
    var hash = location.hash || ''
    var m = hash.match(/id=([0-9a-fA-F-]{36})/)
    if (m) return m[1]
    try {
      var params = new URLSearchParams(hash.split('?')[1] || '')
      return params.get('id')
    } catch (e) {
      return null
    }
  }

  function isDetailsView () {
    var hash = (location.hash || '').toLowerCase()
    return hash.indexOf('/details') >= 0 || hash.indexOf('details?') >= 0 || hash.indexOf('/item') >= 0
  }

  function openModal (title, bodyHtml, buttons) {
    var backdrop = document.createElement('div')
    backdrop.className = 'jv-modal-backdrop'
    var modal = document.createElement('div')
    modal.className = 'jv-modal'
    modal.innerHTML = '<h2></h2><div class="jv-body"></div><div class="jv-row"></div>'
    modal.querySelector('h2').textContent = title
    modal.querySelector('.jv-body').innerHTML = bodyHtml
    var row = modal.querySelector('.jv-row')
    buttons.forEach(function (b) {
      var btn = document.createElement('button')
      btn.type = 'button'
      btn.className = b.primary ? 'raised button-submit' : 'raised'
      btn.textContent = b.label
      btn.addEventListener('click', function () {
        Promise.resolve(b.onClick && b.onClick(modal)).then(function (keepOpen) {
          if (!keepOpen) backdrop.remove()
        })
      })
      row.appendChild(btn)
    })
    backdrop.appendChild(modal)
    backdrop.addEventListener('click', function (e) {
      if (e.target === backdrop) backdrop.remove()
    })
    document.body.appendChild(backdrop)
    return backdrop
  }

  function optionsHtml (list) {
    return (list || []).map(function (r) {
      return '<option value="' + escapeAttr(r) + '">' + escapeHtml(r) + '</option>'
    }).join('')
  }

  function escapeHtml (s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;')
  }

  function escapeAttr (s) {
    return escapeHtml(s).replace(/'/g, '&#39;')
  }

  function showProposeModal (itemId) {
    if (!cfg) return
    openModal('Propose deletion',
      '<p>Choose why this should be removed. Other users will be notified to vote.</p>' +
      '<label>Reason</label><select id="jv-reason">' + optionsHtml(cfg.deleteReasons) + '</select>' +
      '<label>Note (optional / required for Other)</label><textarea id="jv-note" rows="3"></textarea>',
      [
        { label: 'Cancel' },
        {
          label: 'Propose',
          primary: true,
          onClick: function (modal) {
            var reason = modal.querySelector('#jv-reason').value
            var note = modal.querySelector('#jv-note').value
            return api('POST', 'votes', {
              ItemId: itemId,
              DeleteReason: reason,
              DeleteNote: note || null
            }).then(function (res) {
              toast(res.message || 'Vote created')
              injectItemButton()
            }).catch(function (err) {
              toast((err && err.message) || 'Failed to propose')
              return true
            })
          }
        }
      ])
  }

  function showBallotModal (vote) {
    if (!cfg || !vote) return
    openModal('Vote on deletion',
      '<p><strong>' + escapeHtml(vote.itemName) + '</strong><br>Proposed by ' +
      escapeHtml(vote.proposerName) + ': ' + escapeHtml(vote.deleteReason) + '</p>' +
      '<p>Yes ' + vote.yesVotes + ' · No ' + vote.noVotes + ' · need ' + vote.requiredYesVotes + '</p>' +
      '<label>Your vote</label><select id="jv-choice"><option value="Yes">Yes — delete</option><option value="No">No — keep</option></select>' +
      '<label>Keep reason (if No)</label><select id="jv-keep">' + optionsHtml(cfg.keepReasons) + '</select>' +
      '<label>Note</label><textarea id="jv-note" rows="2"></textarea>',
      [
        { label: 'Cancel' },
        {
          label: 'Submit vote',
          primary: true,
          onClick: function (modal) {
            var choice = modal.querySelector('#jv-choice').value
            var body = {
              Choice: choice === 'Yes' ? 0 : 1,
              KeepReason: choice === 'No' ? modal.querySelector('#jv-keep').value : null,
              Note: modal.querySelector('#jv-note').value || null
            }
            return api('POST', 'votes/' + vote.id + '/ballot', body).then(function (res) {
              toast(res.message || 'Vote recorded')
              injectItemButton()
              refreshInbox()
            }).catch(function (err) {
              toast((err && err.message) || 'Failed to vote')
              return true
            })
          }
        }
      ])
  }

  function injectItemButton () {
    if (!cfg || !cfg.enabled || !isDetailsView()) {
      removeItemButton()
      return
    }
    var itemId = getItemIdFromLocation()
    if (!itemId) return

    api('GET', 'item/' + itemId).then(function (state) {
      removeItemButton()
      var host = document.querySelector('.mainDetailButtons, .detailButtons, .itemDetailPage .detailButton')
      var container = document.querySelector('.mainDetailButtons') ||
        document.querySelector('.detailButtons') ||
        document.querySelector('.itemDetailPage')
      if (!container) return

      var btn = document.createElement('button')
      btn.type = 'button'
      btn.id = 'jellyvote-item-btn'
      btn.className = 'raised jv-item-btn'
      if (state.openVote && !state.hasVoted && state.canVote) {
        btn.textContent = 'Vote on delete'
        btn.addEventListener('click', function () { showBallotModal(state.openVote) })
      } else if (state.openVote && state.hasVoted) {
        btn.textContent = 'Delete vote open'
        btn.disabled = true
      } else if (state.canPropose) {
        btn.textContent = 'Vote to delete'
        btn.addEventListener('click', function () { showProposeModal(itemId) })
      } else {
        return
      }

      if (container.classList.contains('mainDetailButtons') || container.classList.contains('detailButtons')) {
        container.appendChild(btn)
      } else {
        btn.style.position = 'relative'
        btn.style.margin = '12px'
        container.insertBefore(btn, container.firstChild)
      }
    }).catch(function (e) { log('item state failed', e) })
  }

  function removeItemButton () {
    var el = document.getElementById('jellyvote-item-btn')
    if (el) el.remove()
  }

  function ensureInboxFab () {
    if (document.getElementById('jellyvote-inbox-fab')) return
    var fab = document.createElement('button')
    fab.id = 'jellyvote-inbox-fab'
    fab.className = 'jv-fab-inbox'
    fab.type = 'button'
    fab.innerHTML = 'Votes<span class="jv-badge" id="jellyvote-badge" style="display:none">0</span>'
    fab.addEventListener('click', openInbox)
    document.body.appendChild(fab)
  }

  function refreshInbox () {
    if (!ApiClient || !ApiClient.isLoggedIn || (ApiClient.isLoggedIn && !ApiClient.isLoggedIn())) {
      // Jellyfin ApiClient.isLoggedIn can be a property or method depending on version
    }
    api('GET', 'alerts').then(function (data) {
      ensureInboxFab()
      var badge = document.getElementById('jellyvote-badge')
      if (!badge) return
      if (data.unread > 0) {
        badge.style.display = 'inline'
        badge.textContent = String(data.unread)
      } else {
        badge.style.display = 'none'
      }
    }).catch(function () { /* not logged in or plugin unavailable */ })
  }

  function openInbox () {
    api('GET', 'alerts').then(function (data) {
      var alerts = data.alerts || []
      var html = alerts.length
        ? '<ul style="padding-left:18px;margin:0;max-height:280px;overflow:auto">' +
          alerts.map(function (a) {
            return '<li style="margin-bottom:8px"><strong>' + escapeHtml(a.title) + '</strong><br>' +
              escapeHtml(a.message) +
              (a.voteId ? '<br><button type="button" data-vote="' + a.voteId + '" class="jv-open-vote">Open vote</button>' : '') +
              '</li>'
          }).join('') + '</ul>'
        : '<p>No notifications.</p>'

      var backdrop = openModal('JellyVote inbox', html, [
        {
          label: 'Mark all read',
          onClick: function () {
            return api('POST', 'alerts/read', { AlertIds: [] }).then(refreshInbox)
          }
        },
        { label: 'Close', primary: true }
      ])

      backdrop.querySelectorAll('.jv-open-vote').forEach(function (btn) {
        btn.addEventListener('click', function () {
          var id = btn.getAttribute('data-vote')
          api('GET', 'votes/' + id).then(function (vote) {
            backdrop.remove()
            if (vote.status === 'Open') showBallotModal(vote)
            else toast('Vote status: ' + vote.status)
          })
        })
      })
    })
  }

  function loadConfig () {
    return api('GET', 'public-config').then(function (c) {
      cfg = c
      return c
    })
  }

  function onRoute () {
    injectItemButton()
  }

  function start () {
    if (!window.ApiClient) {
      setTimeout(start, 400)
      return
    }
    ensureStyles()
    loadConfig().then(function () {
      if (!cfg || !cfg.enabled) return
      ensureInboxFab()
      refreshInbox()
      injectItemButton()
      if (pollTimer) clearInterval(pollTimer)
      pollTimer = setInterval(refreshInbox, 60000)
    }).catch(function (e) { log('init failed', e) })

    window.addEventListener('hashchange', onRoute)
    // Jellyfin SPA view changes
    document.addEventListener('viewshow', onRoute)
  }

  start()
})()
