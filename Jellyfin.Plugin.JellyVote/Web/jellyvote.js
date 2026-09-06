(function () {
  'use strict'

  var PLUGIN = 'JellyVote'
  var cfg = null
  var pollTimer = null
  var injectTimer = null
  var observer = null

  function log () {
    if (window.console && console.debug) {
      console.debug.apply(console, ['[' + PLUGIN + ']'].concat([].slice.call(arguments)))
    }
  }

  function api (method, path, body) {
    var url = ApiClient.getUrl('/JellyVote/' + path)
    var opts = { type: method, url: url, dataType: 'json', headers: {} }
    if (body !== undefined) {
      opts.data = JSON.stringify(body)
      opts.contentType = 'application/json'
    }
    return ApiClient.ajax(opts)
  }

  function pick (obj) {
    if (!obj) return undefined
    for (var i = 1; i < arguments.length; i++) {
      var key = arguments[i]
      if (obj[key] !== undefined && obj[key] !== null) return obj[key]
      var lower = typeof key === 'string' ? key.charAt(0).toLowerCase() + key.slice(1) : key
      if (obj[lower] !== undefined && obj[lower] !== null) return obj[lower]
      var upper = typeof key === 'string' ? key.charAt(0).toUpperCase() + key.slice(1) : key
      if (obj[upper] !== undefined && obj[upper] !== null) return obj[upper]
    }
    return undefined
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
      'max-width:440px;width:100%;padding:18px 18px 14px;box-shadow:0 12px 40px rgba(0,0,0,.45)}',
      '.jv-modal h2{margin:0 0 8px;font-size:1.15rem}',
      '.jv-modal p{margin:0 0 10px;opacity:.85;font-size:.9rem;line-height:1.4}',
      '.jv-modal label{display:block;margin:8px 0 4px;font-size:.85rem;opacity:.85}',
      '.jv-modal select,.jv-modal textarea,.jv-modal input{width:100%;box-sizing:border-box;margin-bottom:8px}',
      '.jv-modal .jv-row{display:flex;gap:8px;justify-content:flex-end;margin-top:12px;flex-wrap:wrap}',
      '.jv-modal button{cursor:pointer}',
      '.jv-msg{border:1px solid rgba(255,255,255,.12);background:rgba(255,255,255,.04);border-radius:10px;',
      'padding:10px 12px;margin:0 0 12px;font-size:.9rem;line-height:1.45}',
      '.jv-msg strong{display:block;margin-bottom:4px}',
      '.jv-toast{position:fixed;left:50%;transform:translateX(-50%);bottom:80px;z-index:10001;',
      'background:rgba(20,20,20,.92);color:#fff;padding:10px 16px;border-radius:8px;max-width:90vw}',
      '#jellyvote-item-btn.detailButton{min-width:auto}',
      '#jellyvote-propose-btn.detailButton{min-width:auto}'
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
    var m = hash.match(/id=([0-9a-fA-F-]{36})/i)
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

  function voteMessageHtml (vote) {
    var name = pick(vote, 'itemName', 'ItemName') || 'This item'
    var proposer = pick(vote, 'proposerName', 'ProposerName') || 'Someone'
    var reason = pick(vote, 'deleteReason', 'DeleteReason') || 'No reason given'
    var note = pick(vote, 'deleteNote', 'DeleteNote')
    var yes = pick(vote, 'yesVotes', 'YesVotes') || 0
    var no = pick(vote, 'noVotes', 'NoVotes') || 0
    var need = pick(vote, 'requiredYesVotes', 'RequiredYesVotes') || 0
    var html = '<div class="jv-msg">' +
      '<strong>' + escapeHtml(name) + '</strong>' +
      escapeHtml(proposer) + ' wants this deleted.<br>' +
      '<em>Reason:</em> ' + escapeHtml(reason)
    if (note) html += '<br><em>Note:</em> ' + escapeHtml(note)
    html += '</div>'
    html += '<p>Current tally: Yes ' + yes + ' · No ' + no + ' · need ' + need + '</p>'
    return html
  }

  function showProposeModal (itemId) {
    if (!cfg) return
    openModal('Propose deletion',
      '<p>Choose why this should be removed. Other users will be notified to vote.</p>' +
      '<label>Reason</label><select id="jv-reason">' + optionsHtml(cfg.deleteReasons || cfg.DeleteReasons) + '</select>' +
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
              toast(pick(res, 'message', 'Message') || 'Vote created')
              scheduleInject()
              refreshInbox()
            }).catch(function (err) {
              toast((err && (err.message || err.Message)) || 'Failed to propose')
              return true
            })
          }
        }
      ])
  }

  function showBallotModal (vote) {
    if (!cfg || !vote) return
    var voteId = pick(vote, 'id', 'Id')
    openModal('Vote on deletion',
      voteMessageHtml(vote) +
      '<label>Your vote</label><select id="jv-choice"><option value="Yes">Yes — delete</option><option value="No">No — keep</option></select>' +
      '<label>Keep reason (if No)</label><select id="jv-keep">' + optionsHtml(cfg.keepReasons || cfg.KeepReasons) + '</select>' +
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
            return api('POST', 'votes/' + voteId + '/ballot', body).then(function (res) {
              toast(pick(res, 'message', 'Message') || 'Vote recorded')
              scheduleInject()
              refreshInbox()
            }).catch(function (err) {
              toast((err && (err.message || err.Message)) || 'Failed to vote')
              return true
            })
          }
        }
      ])
  }

  function findDetailButtonContainer () {
    var selectors = [
      '.mainDetailButtons',
      '.detailButtons',
      '.detailButtonsContainer',
      '.itemDetailPage .detailButtons',
      '.itemDetailPage .mainDetailButtons'
    ]
    for (var i = 0; i < selectors.length; i++) {
      var el = document.querySelector(selectors[i])
      if (el) return el
    }
    return null
  }

  function makeDetailButton (id, iconName, title) {
    var button = document.createElement('button')
    button.type = 'button'
    button.id = id
    button.title = title
    button.setAttribute('aria-label', title)
    button.className = 'button-flat detailButton emby-button'
    button.innerHTML =
      '<div class="detailButton-content">' +
      '<span class="material-icons detailButton-icon" aria-hidden="true">' + iconName + '</span>' +
      '</div>'
    return button
  }

  function placeDetailButton (container, button) {
    var more = container.querySelector('.btnMoreCommands')
    if (more && more.parentNode === container) {
      container.insertBefore(button, more)
    } else {
      container.appendChild(button)
    }
  }

  function removeItemButtons () {
    ;['jellyvote-item-btn', 'jellyvote-propose-btn'].forEach(function (id) {
      var el = document.getElementById(id)
      if (el) el.remove()
    })
  }

  function injectItemButtons () {
    if (!cfg || !cfg.enabled || !isDetailsView()) {
      removeItemButtons()
      return
    }

    var itemId = getItemIdFromLocation()
    if (!itemId) {
      removeItemButtons()
      return
    }

    var container = findDetailButtonContainer()
    if (!container) {
      // Wait for Jellyfin to render the detail action bar — never inject into page body.
      return
    }

    api('GET', 'item/' + itemId).then(function (state) {
      removeItemButtons()
      // Re-find after async; page may have navigated away.
      if (!isDetailsView() || getItemIdFromLocation() !== itemId) return
      container = findDetailButtonContainer()
      if (!container) return

      var openVote = pick(state, 'openVote', 'OpenVote')
      var canVote = !!pick(state, 'canVote', 'CanVote')
      var hasVoted = !!pick(state, 'hasVoted', 'HasVoted')

      // Detail-bar vote button ONLY when a vote is active (never overlays the poster).
      if (!openVote) return

      var voteBtn = makeDetailButton(
        'jellyvote-item-btn',
        hasVoted ? 'how_to_vote' : 'ballot',
        hasVoted ? 'Deletion vote in progress' : 'Vote on deletion'
      )
      if (!hasVoted && canVote) {
        voteBtn.addEventListener('click', function () { showBallotModal(openVote) })
      } else {
        voteBtn.addEventListener('click', function () {
          openModal(
            'Deletion vote in progress',
            voteMessageHtml(openVote) +
              (hasVoted
                ? '<p>You have already voted on this item.</p>'
                : '<p>You are not eligible to vote on this item.</p>'),
            [{ label: 'Close', primary: true }]
          )
        })
      }
      placeDetailButton(container, voteBtn)
    }).catch(function (e) { log('item state failed', e) })
  }

  function scheduleInject () {
    if (injectTimer) clearTimeout(injectTimer)
    injectTimer = setTimeout(function () {
      injectItemButtons()
      // Retry a few times while detail buttons mount.
      var tries = 0
      var retry = setInterval(function () {
        tries++
        if (findDetailButtonContainer() || tries > 12) {
          clearInterval(retry)
          injectItemButtons()
        }
      }, 250)
    }, 50)
  }

  function watchDetailButtons () {
    if (observer) observer.disconnect()
    observer = new MutationObserver(function () {
      if (!isDetailsView()) return
      if (!document.getElementById('jellyvote-item-btn') && !document.getElementById('jellyvote-propose-btn')) {
        if (findDetailButtonContainer()) scheduleInject()
      }
    })
    observer.observe(document.body, { childList: true, subtree: true })
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
    api('GET', 'alerts').then(function (data) {
      ensureInboxFab()
      var badge = document.getElementById('jellyvote-badge')
      if (!badge) return
      var unread = pick(data, 'unread', 'Unread') || 0
      if (unread > 0) {
        badge.style.display = 'inline'
        badge.textContent = String(unread)
      } else {
        badge.style.display = 'none'
      }
    }).catch(function () { /* not logged in or plugin unavailable */ })
  }

  function openInbox () {
    api('GET', 'alerts').then(function (data) {
      var alerts = pick(data, 'alerts', 'Alerts') || []
      var html = alerts.length
        ? '<ul style="padding-left:18px;margin:0;max-height:280px;overflow:auto">' +
          alerts.map(function (a) {
            var voteId = pick(a, 'voteId', 'VoteId')
            return '<li style="margin-bottom:8px"><strong>' + escapeHtml(pick(a, 'title', 'Title')) + '</strong><br>' +
              escapeHtml(pick(a, 'message', 'Message')) +
              (voteId ? '<br><button type="button" data-vote="' + voteId + '" class="jv-open-vote">Open vote</button>' : '') +
              '</li>'
          }).join('') + '</ul>'
        : '<p>No notifications.</p>'

      var itemId = isDetailsView() ? getItemIdFromLocation() : null
      var buttons = [
        {
          label: 'Mark all read',
          onClick: function () {
            return api('POST', 'alerts/read', { AlertIds: [] }).then(refreshInbox)
          }
        },
        { label: 'Close', primary: true }
      ]
      if (itemId) {
        buttons.unshift({
          label: 'Propose delete here',
          onClick: function () {
            showProposeModal(itemId)
          }
        })
      }

      var backdrop = openModal('JellyVote inbox', html, buttons)

      backdrop.querySelectorAll('.jv-open-vote').forEach(function (btn) {
        btn.addEventListener('click', function () {
          var id = btn.getAttribute('data-vote')
          api('GET', 'votes/' + id).then(function (vote) {
            backdrop.remove()
            var status = pick(vote, 'status', 'Status')
            if (String(status).toLowerCase() === 'open') showBallotModal(vote)
            else toast('Vote status: ' + status)
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
    scheduleInject()
  }

  function start () {
    if (!window.ApiClient) {
      setTimeout(start, 400)
      return
    }
    ensureStyles()
    watchDetailButtons()
    loadConfig().then(function () {
      if (!cfg || !(cfg.enabled || cfg.Enabled)) return
      ensureInboxFab()
      refreshInbox()
      scheduleInject()
      if (pollTimer) clearInterval(pollTimer)
      pollTimer = setInterval(function () {
        refreshInbox()
        if (isDetailsView()) scheduleInject()
      }, 60000)
    }).catch(function (e) { log('init failed', e) })

    window.addEventListener('hashchange', onRoute)
    document.addEventListener('viewshow', onRoute)
  }

  start()
})()
