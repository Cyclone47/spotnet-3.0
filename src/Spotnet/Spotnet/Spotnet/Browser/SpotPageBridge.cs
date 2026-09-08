namespace Spotnet.Browser;

/// <summary>
/// The script that connects a spot document to its host page under WebView2.
/// </summary>
/// <remarks>
/// MSHTML let the host hold <c>HtmlElement</c> references, attach .NET handlers to them
/// and read the DOM synchronously. WebView2 has none of that: the document lives in
/// another process and every crossing is asynchronous. This script is the whole of the
/// crossing.
///
/// Page to host is one channel, <c>window.chrome.webview.postMessage</c>, carrying a JSON
/// object with a <c>type</c>. Host to page is a small set of functions on
/// <c>window.spotnet</c>, called with <c>ExecuteScriptAsync</c>.
///
/// Two rules shape it. Anything the host would otherwise have to read back is gathered
/// here and sent along with the message that needs it - a Send click carries the nickname
/// and the comment body, a quote click carries the quoted text and its author - so the
/// host never has to ask a question and wait for the answer mid-flow. And every listener
/// is delegated from <c>document</c>, so panels the host rewrites wholesale (the preview,
/// the smiley strip) keep working without rebinding anything.
///
/// The document renders spot bodies and comments straight off Usenet, so the script
/// treats the page as untrusted: it forwards a fixed set of message types and never
/// evaluates anything the page supplies. The host validates the payloads again.
/// </remarks>
internal static class SpotPageBridge
{
	/// <summary>Anchor schemes the host handles instead of the browser.</summary>
	/// <remarks>
	/// The themes have always driven the host through links like <c>href='ubb:b'</c>.
	/// Under MSHTML the host caught them as cancelled navigations. Catching the click
	/// here instead keeps the themes untouched, and does not depend on how WebView2
	/// chooses to treat a navigation to an unknown scheme.
	/// </remarks>
	internal const string Script = @"
(function () {
    'use strict';

    var HOST_SCHEMES = [
        'link:', 'query:', 'menu:', 'spotnet:', 'loadimg:', 'quote:', 'reply:',
        'smiley:', 'ubb:', 'show:', 'addtoblack:', 'spamreports:'
    ];

    var _imageIsFullSize = false;

    // Buttons the host reacts to that are not plain links.
    var CLICK_IDS = [
        'AddComment', 'DownloadButton', 'SpotImage', 'ReportButton', 'FavButton',
        'ClosePreview', 'CloseImdb', 'CloseSmiles',
        'TranslatePostBtn', 'TranslateCommentsBtn', 'TranslateAllBtn'
    ];

    function post(message) {
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(JSON.stringify(message));
            }
        } catch (e) {
            // A failed post must never break the page.
        }
    }

    function byId(id) {
        return document.getElementById(id);
    }

    function value(id) {
        var el = byId(id);
        return el && typeof el.value === 'string' ? el.value : '';
    }

    function nickname() {
        var el = byId('Nickname');
        if (el && typeof el.value === 'string') {
            return el.value;
        }
        var named = document.getElementsByName('Nickname');
        return named.length && typeof named[0].value === 'string' ? named[0].value : '';
    }

    function isEditable(el) {
        if (!el) {
            return false;
        }
        var tag = (el.tagName || '').toUpperCase();
        return tag === 'TEXTAREA' || tag === 'INPUT' || el.isContentEditable === true;
    }

    function closestAnchor(el) {
        while (el && el !== document) {
            if ((el.tagName || '').toUpperCase() === 'A') {
                return el;
            }
            el = el.parentNode;
        }
        return null;
    }

    function rawHref(anchor) {
        // getAttribute, not .href: the browser resolves an unknown scheme into
        // something else entirely, and the host matches on the literal text.
        return anchor ? (anchor.getAttribute('href') || '') : '';
    }

    function isHostScheme(href) {
        var lower = href.toLowerCase();
        for (var i = 0; i < HOST_SCHEMES.length; i++) {
            if (lower.indexOf(HOST_SCHEMES[i]) === 0) {
                return true;
            }
        }
        return false;
    }

    // --- comment lookup ----------------------------------------------------

    // Walks up to the element wrapping one whole comment, matching the shape the
    // themes produce: a table, or a header whose parent is the wrapper.
    function commentRoot(el) {
        while (el && el !== document) {
            var tag = (el.tagName || '').toUpperCase();
            if (tag === 'TABLE') {
                return el;
            }
            if ((el.getAttribute && el.getAttribute('name') || '').toLowerCase() === 'header') {
                return el.parentNode;
            }
            el = el.parentNode;
        }
        return null;
    }

    function childByHref(root, prefix) {
        if (!root) {
            return null;
        }
        var anchors = root.getElementsByTagName('a');
        for (var i = 0; i < anchors.length; i++) {
            if (rawHref(anchors[i]).toLowerCase().indexOf(prefix) === 0) {
                return anchors[i];
            }
        }
        return null;
    }

    // The sender of the comment an element sits in, taken from its menu: link.
    function senderOf(root) {
        var menu = childByHref(root, 'menu:');
        return menu ? rawHref(menu).substring('menu:'.length) : '';
    }

    // The rendered body of a comment, addressed as 'd' + the id in its quote/reply link.
    function bodyOf(href) {
        var separator = href.indexOf(':');
        if (separator < 0) {
            return '';
        }
        var id = href.substring(separator + 2);
        var body = byId('d' + id);
        return body ? body.innerHTML : '';
    }

    // --- text insertion ----------------------------------------------------

    function insertAtCaret(el, text) {
        if (!el) {
            return;
        }
        el.focus();
        var start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
        var end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
        el.value = el.value.substring(0, start) + text + el.value.substring(end);
        var caret = start + text.length;
        try {
            el.setSelectionRange(caret, caret);
        } catch (e) {
            // Selection APIs are unavailable on some input types; the text is in.
        }
        notifyInput();
    }

    function notifyInput() {
        post({ type: 'input', nickname: nickname(), body: value('CommentBody') });
    }

    // --- translation -------------------------------------------------------

    var _transLabels = {};
    var _postState = 'original'; // 'original' | 'translating' | 'translated'
    var _originalPostHtml = null;
    var _translatedPostHtml = null;

    var _commentsState = 'original'; // 'original' | 'translating' | 'translated'
    var _originalComments = {};
    var _translatedComments = {};

    function escapeAttr(str) {
        return (str || '').replace(/&/g, '&amp;').replace(/""/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function togglePostTranslation() {
        var desc = byId('Description');
        if (!desc) {
            return;
        }

        if (_postState === 'translating') {
            return;
        }

        if (_postState === 'translated') {
            desc.innerHTML = _originalPostHtml;
            _postState = 'original';
            var btnText = byId('TranslatePostBtnText');
            if (btnText) {
                btnText.textContent = _transLabels.translatePost || 'Translate post';
            }
            var badge = byId('TranslatePostBadge');
            if (badge) {
                badge.style.display = 'none';
            }
            return;
        }

        if (_postState === 'original' && _translatedPostHtml) {
            desc.innerHTML = _translatedPostHtml;
            _postState = 'translated';
            var btnText = byId('TranslatePostBtnText');
            if (btnText) {
                btnText.textContent = _transLabels.showOriginal || 'Show original';
            }
            var badge = byId('TranslatePostBadge');
            if (badge) {
                badge.style.display = 'inline-flex';
            }
            return;
        }

        _originalPostHtml = desc.innerHTML;
        _postState = 'translating';
        var btn = byId('TranslatePostBtn');
        if (btn) {
            btn.disabled = true;
        }
        var btnText = byId('TranslatePostBtnText');
        if (btnText) {
            btnText.textContent = _transLabels.translating || 'Translating...';
        }

        post({ type: 'translate_post', html: _originalPostHtml });
    }

    function toggleCommentsTranslation() {
        if (_commentsState === 'translating') {
            return;
        }

        if (_commentsState === 'translated') {
            for (var id in _originalComments) {
                var el = byId(id);
                if (el) {
                    el.innerHTML = _originalComments[id];
                }
            }
            _commentsState = 'original';
            var btnText = byId('TranslateCommentsBtnText');
            if (btnText) {
                btnText.textContent = _transLabels.translateComments || 'Translate comments';
            }
            var badge = byId('TranslateCommentsBadge');
            if (badge) {
                badge.style.display = 'none';
            }
            return;
        }

        var commentsContainer = byId('Comments');
        if (!commentsContainer) {
            return;
        }

        var spans = commentsContainer.getElementsByTagName('span');
        var toTranslate = {};
        for (var i = 0; i < spans.length; i++) {
            var sId = spans[i].id;
            if (sId && sId.indexOf('d') === 0 && sId.length > 1) {
                if (!_originalComments[sId]) {
                    _originalComments[sId] = spans[i].innerHTML;
                }
                if (_translatedComments[sId]) {
                    spans[i].innerHTML = _translatedComments[sId];
                } else {
                    toTranslate[sId] = spans[i].innerHTML;
                }
            }
        }

        var toTranslateKeys = Object.keys(toTranslate);
        if (toTranslateKeys.length === 0) {
            if (Object.keys(_originalComments).length > 0) {
                _commentsState = 'translated';
                var btnText = byId('TranslateCommentsBtnText');
                if (btnText) {
                    btnText.textContent = _transLabels.showOriginal || 'Show original';
                }
                var badge = byId('TranslateCommentsBadge');
                if (badge) {
                    badge.style.display = 'inline-flex';
                }
            }
            return;
        }

        _commentsState = 'translating';
        var btn = byId('TranslateCommentsBtn');
        if (btn) {
            btn.disabled = true;
        }
        var btnText = byId('TranslateCommentsBtnText');
        if (btnText) {
            btnText.textContent = _transLabels.translating || 'Translating...';
        }

        post({ type: 'translate_comments', comments: toTranslate });
    }

    function translateAll() {
        if (_postState === 'translated' && _commentsState === 'translated') {
            togglePostTranslation();
            toggleCommentsTranslation();
        } else {
            if (_postState !== 'translated') {
                togglePostTranslation();
            }
            if (_commentsState !== 'translated') {
                toggleCommentsTranslation();
            }
        }
    }

    // --- host to page ------------------------------------------------------

    var api = {
        setHtml: function (id, html) {
            var el = byId(id);
            if (el) {
                el.innerHTML = html;
            }
        },
        getHtml: function (id) {
            var el = byId(id);
            return el ? el.innerHTML : null;
        },
        setOuterHtml: function (id, html) {
            var el = byId(id);
            if (el) {
                el.outerHTML = html;
            }
        },
        setText: function (id, text) {
            var el = byId(id);
            if (el && el.textContent !== text) {
                el.textContent = text;
            }
        },
        setStyle: function (id, css) {
            var el = byId(id);
            if (el) {
                el.setAttribute('style', css);
            }
        },
        prependStyle: function (id, css) {
            var el = byId(id);
            if (el) {
                el.setAttribute('style', css + (el.getAttribute('style') || ''));
            }
        },
        setAttr: function (id, name, val) {
            var el = byId(id);
            if (el) {
                el.setAttribute(name, val);
            }
        },
        setValue: function (id, val) {
            var el = byId(id);
            if (el) {
                el.value = val;
            }
        },
        setClass: function (id, name) {
            var el = byId(id);
            if (el) {
                el.className = name;
            }
        },
        // The themes carry the enabled/disabled state in the class list, so the swap is
        // done here rather than reading the class back to the host first.
        setButtonEnabled: function (id, enabled, title) {
            var el = byId(id);
            if (!el) {
                return;
            }
            var name = (el.className || '').replace(' enabled', '').replace(' disabled', '');
            el.className = name + (enabled ? ' enabled' : ' disabled');
            el.setAttribute('title', title);
        },
        focusElement: function (id) {
            var el = byId(id);
            if (el) {
                el.focus();
            }
        },
        appendComment: function (html) {
            var comments = byId('Comments');
            if (comments) {
                comments.insertAdjacentHTML('beforeend', html);
            }
        },
        insertIntoComment: function (text) {
            insertAtCaret(byId('CommentBody'), text);
        },

        // Wraps whatever is selected in the comment box in a UBB tag. The host has
        // already checked the tag against its allowlist.
        applyUbb: function (tag, linkTarget) {
            var el = byId('CommentBody');
            if (!el) {
                return;
            }
            el.focus();
            var start = typeof el.selectionStart === 'number' ? el.selectionStart : el.value.length;
            var end = typeof el.selectionEnd === 'number' ? el.selectionEnd : el.value.length;
            var selected = el.value.substring(start, end);
            var replacement;
            if (tag === 'l') {
                replacement = '[url=""' + linkTarget + '""]' + selected + '[/url]';
            } else if (tag === 'c') {
                replacement = '[color=#' + value('ColorInput') + ']' + selected + '[/color]';
            } else {
                replacement = '[' + tag + ']' + selected + '[/' + tag + ']';
            }
            el.value = el.value.substring(0, start) + replacement + el.value.substring(end);
            var caret = start + replacement.length;
            try {
                el.setSelectionRange(caret, caret);
            } catch (e) {
                // See insertAtCaret.
            }
            notifyInput();
        },

        // Calls the theme's own smiley() helper, which knows the markup it wants.
        callSmiley: function (code) {
            var el = byId('CommentBody');
            if (el) {
                el.focus();
            }
            if (typeof window.smiley === 'function') {
                window.smiley(code);
            } else {
                insertAtCaret(el, ' :' + code + ': ');
                return;
            }
            notifyInput();
        },

        // The full-size toggle needs the image's rendered size and the viewport, both of
        // which only exist in the page, so the whole toggle lives here.
        toggleImageSize: function (fullSize) {
            var img = byId('SpotImage');
            if (!img) {
                return false;
            }
            var name = (img.className || '').replace(' full', '');
            if (!fullSize) {
                img.setAttribute('style', '');
                img.className = name;
                _imageIsFullSize = false;
                return false;
            }
            var height = img.height || img.naturalHeight || 1;
            var width = img.width || img.naturalWidth || 1;
            var parent = img.parentNode;
            if (parent && parent.setAttribute) {
                var parentStyle = parent.getAttribute('style') || '';
                if (parentStyle.indexOf('min-height') < 0) {
                    parent.setAttribute('style', 'min-height: ' + height + 'px;' + parentStyle);
                }
            }
            img.className = name + ' full';
            var tall = (window.innerHeight / window.innerWidth) > (height / width);
            img.setAttribute('style', tall ? 'min-width: 90%' : 'min-height: 90%');
            _imageIsFullSize = true;
            return true;
        },

        // Recolours every comment by one author after a black/white list change.
        updateCommentAuthor: function (modulus, className, blackLinkText, hideAvatarAndDesc, hideBlackLink) {
            var comments = byId('Comments');
            if (!comments) {
                return;
            }
            var anchors = comments.getElementsByTagName('a');
            for (var i = 0; i < anchors.length; i++) {
                if (rawHref(anchors[i]).indexOf('menu:' + modulus) !== 0) {
                    continue;
                }
                var root = commentRoot(anchors[i]);
                if (!root) {
                    continue;
                }
                anchors[i].className = className;
                var black = childByHref(root, 'addtoblack:');
                if (black) {
                    if (blackLinkText !== null) {
                        black.textContent = blackLinkText;
                    }
                    if (hideBlackLink !== null) {
                        black.setAttribute('style', hideBlackLink ? 'display:none' : 'display:true');
                    }
                }
                if (hideAvatarAndDesc !== null) {
                    var visibility = hideAvatarAndDesc ? 'display:none' : 'display:true';
                    var images = root.getElementsByTagName('img');
                    for (var j = 0; j < images.length; j++) {
                        var src = images[j].getAttribute('src') || '';
                        if (src.indexOf('http://www.gravatar.com/avatar/') === 0 || src.indexOf('data:image/') === 0) {
                            images[j].setAttribute('style', visibility);
                        }
                    }
                    var reply = childByHref(root, 'reply:');
                    if (reply) {
                        var id = rawHref(reply).substring('reply:'.length + 1);
                        var desc = byId('d' + id);
                        if (desc) {
                            desc.setAttribute('style', visibility);
                        }
                    }
                }
            }
        },

        scrollToComment: function () {
            if (document.body) {
                document.body.scrollTop = document.body.scrollHeight;
            }
            var el = byId('CommentBody');
            if (el) {
                el.focus();
            }
        },

        clearSelection: function () {
            try {
                var selection = window.getSelection();
                if (selection) {
                    selection.removeAllRanges();
                }
            } catch (e) {
                // Nothing selected, nothing to do.
            }
        },

        initTranslationUi: function (labels) {
            _transLabels = labels || {};
            function mount() {
                var desc = byId('Description');
                if (desc && !byId('sn-post-trans-bar')) {
                    var bar = document.createElement('div');
                    bar.id = 'sn-post-trans-bar';
                    bar.style.cssText = 'margin: 8px 0 10px 0; display: flex; align-items: center; flex-wrap: wrap; width: 100%; box-sizing: border-box; overflow: hidden; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; font-size: 12px;';
                    bar.innerHTML =
                        '<button type=""button"" id=""TranslatePostBtn"" style=""background:#2e71b8; color:#fff; border:1px solid #20558e; border-radius:4px; padding:3px 9px; font-size:12px; font-weight:500; cursor:pointer; display:inline-flex; align-items:center; gap:4px; font-family:inherit; margin-right:6px; margin-bottom:4px; flex-shrink:0;"">' +
                            '<span>🌐</span> <span id=""TranslatePostBtnText"">' + escapeAttr(_transLabels.translatePost || 'Translate post') + '</span>' +
                        '</button>' +
                        '<button type=""button"" id=""TranslateAllBtn"" style=""background:transparent; color:#2e71b8; border:1px solid #2e71b8; border-radius:4px; padding:3px 8px; font-size:11px; cursor:pointer; display:inline-flex; align-items:center; gap:3px; font-family:inherit; margin-right:6px; margin-bottom:4px; flex-shrink:0;"">' +
                            '<span>⚡</span> <span id=""TranslateAllBtnText"">' + escapeAttr(_transLabels.translateAll || 'Translate all') + '</span>' +
                        '</button>' +
                        '<span id=""TranslatePostBadge"" style=""display:none; align-items:center; gap:4px; font-size:11px; opacity:0.85; flex-shrink:1; min-width:0; overflow:hidden; white-space:nowrap; margin-bottom:4px;"">' +
                            '<span style=""overflow:hidden; text-overflow:ellipsis;"">' + escapeAttr(_transLabels.automatedLabel || 'Automatically translated') + '</span>' +
                            '<span title=""' + escapeAttr(_transLabels.disclaimer || '') + '"" style=""display:inline-block; flex-shrink:0; width:14px; height:14px; line-height:14px; text-align:center; border-radius:50%; background:#6c757d; color:#fff; font-weight:bold; font-size:10px; cursor:help;"">i</span>' +
                        '</span>';
                    desc.parentNode.insertBefore(bar, desc);
                }

                var commentsContainer = byId('Comments') || byId('CommentsProgress');
                if (commentsContainer && !byId('sn-comments-trans-bar')) {
                    var cBar = document.createElement('div');
                    cBar.id = 'sn-comments-trans-bar';
                    cBar.style.cssText = 'margin: 10px 0; display: flex; align-items: center; flex-wrap: wrap; width: 100%; box-sizing: border-box; overflow: hidden; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; font-size: 12px;';
                    cBar.innerHTML =
                        '<button type=""button"" id=""TranslateCommentsBtn"" style=""background:#2e71b8; color:#fff; border:1px solid #20558e; border-radius:4px; padding:3px 9px; font-size:12px; font-weight:500; cursor:pointer; display:inline-flex; align-items:center; gap:4px; font-family:inherit; margin-right:6px; margin-bottom:4px; flex-shrink:0;"">' +
                            '<span>🌐</span> <span id=""TranslateCommentsBtnText"">' + escapeAttr(_transLabels.translateComments || 'Translate comments') + '</span>' +
                        '</button>' +
                        '<span id=""TranslateCommentsBadge"" style=""display:none; align-items:center; gap:4px; font-size:11px; opacity:0.85; flex-shrink:1; min-width:0; overflow:hidden; white-space:nowrap; margin-bottom:4px;"">' +
                            '<span style=""overflow:hidden; text-overflow:ellipsis;"">' + escapeAttr(_transLabels.automatedLabel || 'Automatically translated') + '</span>' +
                            '<span title=""' + escapeAttr(_transLabels.disclaimer || '') + '"" style=""display:inline-block; flex-shrink:0; width:14px; height:14px; line-height:14px; text-align:center; border-radius:50%; background:#6c757d; color:#fff; font-weight:bold; font-size:10px; cursor:help;"">i</span>' +
                        '</span>';
                    commentsContainer.parentNode.insertBefore(cBar, commentsContainer);
                }
            }
            if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', mount);
            } else {
                mount();
            }
            setTimeout(mount, 300);
        },

        applyPostTranslation: function (success, translatedHtml) {
            var btn = byId('TranslatePostBtn');
            if (btn) {
                btn.disabled = false;
            }
            var btnText = byId('TranslatePostBtnText');
            var badge = byId('TranslatePostBadge');
            var desc = byId('Description');

            if (success && translatedHtml) {
                _translatedPostHtml = translatedHtml;
                if (desc) {
                    desc.innerHTML = translatedHtml;
                }
                _postState = 'translated';
                if (btnText) {
                    btnText.textContent = _transLabels.showOriginal || 'Show original';
                }
                if (badge) {
                    badge.style.display = 'inline-flex';
                }
            } else {
                _postState = 'original';
                if (btnText) {
                    btnText.textContent = _transLabels.failed || 'Translation failed';
                }
                setTimeout(function () {
                    if (_postState === 'original' && btnText) {
                        btnText.textContent = _transLabels.translatePost || 'Translate post';
                    }
                }, 3000);
            }
        },

        applyCommentsTranslation: function (success, translatedMap) {
            var btn = byId('TranslateCommentsBtn');
            if (btn) {
                btn.disabled = false;
            }
            var btnText = byId('TranslateCommentsBtnText');
            var badge = byId('TranslateCommentsBadge');

            if (success && translatedMap) {
                for (var id in translatedMap) {
                    _translatedComments[id] = translatedMap[id];
                    var el = byId(id);
                    if (el) {
                        el.innerHTML = translatedMap[id];
                    }
                }
                _commentsState = 'translated';
                if (btnText) {
                    btnText.textContent = _transLabels.showOriginal || 'Show original';
                }
                if (badge) {
                    badge.style.display = 'inline-flex';
                }
            } else {
                _commentsState = 'original';
                if (btnText) {
                    btnText.textContent = _transLabels.failed || 'Translation failed';
                }
                setTimeout(function () {
                    if (_commentsState === 'original' && btnText) {
                        btnText.textContent = _transLabels.translateComments || 'Translate comments';
                    }
                }, 3000);
            }
        }
    };

    window.spotnet = api;

    // --- page to host ------------------------------------------------------

    document.addEventListener('click', function (e) {
        var target = e.target;
        var anchor = closestAnchor(target);
        if (anchor) {
            var href = rawHref(anchor);
            if (isHostScheme(href)) {
                e.preventDefault();
                e.stopPropagation();
                var lower = href.toLowerCase();
                if (lower.indexOf('quote:') === 0) {
                    var quoteRoot = commentRoot(anchor);
                    post({ type: 'quote', sender: senderOf(quoteRoot), body: bodyOf(href) });
                } else if (lower.indexOf('reply:') === 0) {
                    var replyRoot = commentRoot(anchor);
                    post({ type: 'reply', sender: senderOf(replyRoot) });
                } else {
                    post({ type: 'nav', url: href });
                }
                return;
            }
        }

        var el = target;
        while (el && el !== document) {
            var id = el.id;
            if (id && CLICK_IDS.indexOf(id) >= 0) {
                // A disabled button is inert here rather than at the host, so a
                // double click cannot queue a second post while the first is in flight.
                if ((el.className || '').indexOf('disabled') >= 0 || el.disabled) {
                    return;
                }
                if (id === 'TranslatePostBtn') {
                    togglePostTranslation();
                    return;
                }
                if (id === 'TranslateCommentsBtn') {
                    toggleCommentsTranslation();
                    return;
                }
                if (id === 'TranslateAllBtn') {
                    translateAll();
                    return;
                }
                if (id === 'AddComment') {
                    post({ type: 'click', id: id, nickname: nickname(), body: value('CommentBody') });
                } else {
                    post({ type: 'click', id: id });
                }
                return;
            }
            el = el.parentNode;
        }
    }, true);

    document.addEventListener('keyup', function (e) {
        var id = e.target && e.target.id;
        var name = e.target && e.target.getAttribute ? e.target.getAttribute('name') : null;
        if (id === 'CommentBody' || id === 'Nickname' || name === 'Nickname') {
            notifyInput();
        }
    }, true);

    document.addEventListener('contextmenu', function (e) {
        if (isEditable(document.activeElement)) {
            return;
        }
        e.preventDefault();
        var anchor = closestAnchor(e.target);
        var href = rawHref(anchor);
        post({ type: 'contextmenu', href: href.toLowerCase().indexOf('menu:') === 0 ? href : '' });
    }, true);

    document.addEventListener('mouseup', function (e) {
        if (e.button !== 0 || isEditable(document.activeElement)) {
            return;
        }
        var selected = '';
        try {
            selected = String(window.getSelection());
        } catch (err) {
            selected = '';
        }
        if (selected) {
            post({ type: 'select', text: selected });
        }
    }, true);

    document.addEventListener('keydown', function (e) {
        if (_imageIsFullSize && (e.key === 'Escape' || e.keyCode === 27)) {
            e.preventDefault();
            e.stopPropagation();
            post({ type: 'imageclose' });
        }
    }, true);

    document.addEventListener('click', function (e) {
        if (!_imageIsFullSize) {
            return;
        }
        var img = byId('SpotImage');
        if (!img) {
            return;
        }
        // If the click target is not the image itself (or a child of it),
        // the user clicked on the background - close the full-size view.
        var el = e.target;
        while (el && el !== document) {
            if (el === img) {
                return;
            }
            el = el.parentNode;
        }
        post({ type: 'imageclose' });
    }, false);
})();
";
}
