/* Pitch Meetings – shows the matching Pitch Meeting on movie/series pages and the movie on the Pitch Meeting page.
 * The section is built from jellyfin-web's own markup (verticalSection, emby-scroller, emby-itemscontainer, card),
 * so themes style it like every other row and jellyfin-web handles clicks (open, play) itself. */
(function () {
    'use strict';

    var cfg = window.PitchMeetingsConfig || {};
    if (window.PitchMeetings || cfg.enabled === false) return;

    var SECTION_CLASS = 'pm-section';
    var de = /^de\b/i.test(document.documentElement.lang || navigator.language || '');
    var T = de
        ? { pitch: 'Pitch Meeting', pitches: 'Pitch Meetings', Movie: 'Zum Film', Series: 'Zur Serie', Season: 'Zur Staffel', other: 'Zum Titel' }
        : { pitch: 'Pitch Meeting', pitches: 'Pitch Meetings', Movie: 'The movie', Series: 'The show', Season: 'The season', other: 'Related' };

    var cache = {};      // itemId -> Promise<links>
    var renderSeq = 0;

    function api() { return window.ApiClient; }

    function currentItemId() {
        var h = location.hash || '';
        if (!/^#\/?details\b/i.test(h)) return null;
        var m = /[?&]id=([0-9a-f-]{32,36})/i.exec(h);
        return m ? m[1].replace(/-/g, '').toLowerCase() : null;
    }

    function activePage() {
        var pages = document.querySelectorAll('.itemDetailPage');
        for (var i = pages.length - 1; i >= 0; i--) {
            if (!pages[i].classList.contains('hide')) return pages[i];
        }
        return null;
    }

    function fetchLinks(id) {
        if (!cache[id]) {
            var c = api();
            cache[id] = c.getJSON(c.getUrl('PitchMeetings/Links/' + id)).catch(function (e) {
                delete cache[id];
                throw e;
            });
        }
        return cache[id];
    }

    function fetchItems(ids) {
        var c = api();
        return c.getItems(c.getCurrentUserId(), {
            Ids: ids.join(','),
            Fields: 'PrimaryImageAspectRatio,ProductionYear,PremiereDate',
            EnableUserData: true,
            EnableImageTypes: 'Primary,Thumb,Backdrop'
        }).then(function (r) {
            var byId = {};
            (r.Items || []).forEach(function (it) { byId[it.Id] = it; });
            // keep the server's order (newest Pitch Meeting first), drop what this user cannot see
            return ids.map(function (id) { return byId[String(id).replace(/-/g, '')] || byId[id]; }).filter(Boolean);
        });
    }

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (ch) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
        });
    }

    function imageUrl(item, landscape) {
        var c = api();
        var w = landscape ? 480 : 300;
        var tags = item.ImageTags || {};
        if (landscape) {
            if (tags.Primary) return c.getScaledImageUrl(item.Id, { type: 'Primary', tag: tags.Primary, maxWidth: w });
            if (tags.Thumb) return c.getScaledImageUrl(item.Id, { type: 'Thumb', tag: tags.Thumb, maxWidth: w });
            if (item.BackdropImageTags && item.BackdropImageTags.length) {
                return c.getScaledImageUrl(item.Id, { type: 'Backdrop', tag: item.BackdropImageTags[0], maxWidth: w });
            }
            return '';
        }
        if (tags.Primary) return c.getScaledImageUrl(item.Id, { type: 'Primary', tag: tags.Primary, maxWidth: w });
        if (item.SeriesPrimaryImageTag && item.SeriesId) {
            return c.getScaledImageUrl(item.SeriesId, { type: 'Primary', tag: item.SeriesPrimaryImageTag, maxWidth: w });
        }
        return '';
    }

    function secondaryText(item, isPitch) {
        if (isPitch) {
            if (item.PremiereDate) {
                try { return new Date(item.PremiereDate).toLocaleDateString(de ? 'de-DE' : undefined); } catch (e) { /* ignore */ }
            }
            return '';
        }
        if (item.Type === 'Season') return item.SeriesName || '';
        return item.ProductionYear || '';
    }

    function cardHtml(item, landscape, isPitch) {
        var serverId = item.ServerId || api().serverId();
        var href = '#/details?id=' + item.Id + '&serverId=' + serverId;
        var img = imageUrl(item, landscape);
        var shape = landscape ? 'overflowBackdrop' : 'overflowPortrait';
        var ud = item.UserData || {};
        var indicator = ud.Played
            ? '<div class="cardIndicators"><div class="playedIndicator indicator"><span class="material-icons indicatorIcon check" aria-hidden="true"></span></div></div>'
            : '';
        var progress = !ud.Played && ud.PlayedPercentage
            ? '<div class="innerCardFooter fullInnerCardFooter innerCardFooterClear"><div class="itemProgressBar"><div class="itemProgressBarForeground" style="width:' + ud.PlayedPercentage + '%;"></div></div></div>'
            : '';
        var playable = item.MediaType === 'Video' || item.IsFolder;
        var playButton = playable
            ? '<button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light cardOverlayFab-primary" data-action="resume" title="Play">'
                + '<span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover play_arrow" aria-hidden="true"></span></button>'
            : '';
        return '<div class="card ' + shape + 'Card card-hoverable card-withuserdata pm-card" data-index="0"'
            + ' data-id="' + esc(item.Id) + '" data-serverid="' + esc(serverId) + '" data-type="' + esc(item.Type) + '"'
            + ' data-mediatype="' + esc(item.MediaType || '') + '" data-isfolder="' + (item.IsFolder ? 'true' : 'false') + '"'
            + (item.SeriesId ? ' data-seriesid="' + esc(item.SeriesId) + '"' : '')
            + ' data-action="link">'
            + '<div class="cardBox cardBox-bottompadded">'
            + '<div class="cardScalable">'
            + '<div class="cardPadder cardPadder-' + shape + '"></div>'
            + '<a href="' + esc(href) + '" data-action="link" class="cardImageContainer coveredImage cardContent itemAction"'
            + (img ? ' style="background-image:url(\'' + esc(img) + '\')"' : '') + ' aria-label="' + esc(item.Name) + '">'
            + (img ? '' : '<span class="cardImageIcon material-icons ' + (isPitch ? 'videocam' : 'movie') + '" aria-hidden="true"></span>')
            + indicator + progress + '</a>'
            + '<div class="cardOverlayContainer itemAction" data-action="link">'
            + '<a href="' + esc(href) + '" class="cardImageContainer"></a>' + playButton + '</div>'
            + '</div>'
            + '<div class="cardText cardTextCentered cardText-first"><bdi>' + esc(item.Name) + '</bdi></div>'
            + '<div class="cardText cardTextCentered cardText-secondary"><bdi>' + esc(secondaryText(item, isPitch)) + '</bdi></div>'
            + '</div></div>';
    }

    function sectionHtml(title, cardsHtml) {
        return '<h2 class="sectionTitle sectionTitle-cards padded-right">' + esc(title) + '</h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true">'
            + '<div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer">' + cardsHtml + '</div>'
            + '</div>';
    }

    function removeSections(root) {
        (root || document).querySelectorAll('.' + SECTION_CLASS).forEach(function (n) { n.remove(); });
    }

    function insertSection(page, html, itemId) {
        var container = page.querySelector('.detailPageSecondaryContainer');
        if (!container) return;
        removeSections(page);
        var section = document.createElement('div');
        section.className = 'verticalSection detailVerticalSection ' + SECTION_CLASS;
        section.setAttribute('data-pm-item', itemId);
        section.innerHTML = html;
        // after the seasons/episodes list and "additional parts", before "more from this season", cast, similar …
        var before = container.querySelector('.moreFromSeasonSection')
            || container.querySelector('#castCollapsible')
            || null;
        container.insertBefore(section, before);
    }

    function render() {
        var seq = ++renderSeq;
        var id = currentItemId();
        if (!id || !api() || !api().getCurrentUserId || !api().getCurrentUserId()) {
            removeSections();
            return;
        }

        waitForPage(id, seq).then(function (page) {
            if (!page || seq !== renderSeq) return;
            var existing = page.querySelector('.' + SECTION_CLASS);
            if (existing && existing.getAttribute('data-pm-item') === id) return;
            removeSections(page);

            fetchLinks(id).then(function (links) {
                if (seq !== renderSeq) return null;
                var isPitch = links.kind === 'pitch';
                if (isPitch && cfg.showOnEpisodePage === false) return null;
                if (!isPitch && cfg.showOnTargetPage === false) return null;
                var ids = isPitch ? links.targets : links.pitchMeetings;
                if (!ids || !ids.length) return null;
                return fetchItems(ids).then(function (items) {
                    if (seq !== renderSeq || !items.length) return;
                    var title = isPitch
                        ? (T[items[0].Type] || T.other)
                        : (items.length > 1 ? T.pitches : T.pitch);
                    var cards = items.map(function (it) { return cardHtml(it, isPitch ? false : true, !isPitch); }).join('');
                    insertSection(page, sectionHtml(title, cards), id);
                });
            }).catch(function (e) {
                console.warn('[PitchMeetings]', e);
            });
        });
    }

    // the details page is rendered asynchronously: wait until it shows this item
    function waitForPage(id, seq) {
        return new Promise(function (resolve) {
            var tries = 0;
            (function check() {
                if (seq !== renderSeq) return resolve(null);
                var page = activePage();
                if (page && page.querySelector('.detailPageSecondaryContainer')) return resolve(page);
                if (++tries > 100) return resolve(null);
                setTimeout(check, 100);
            })();
        });
    }

    document.addEventListener('viewshow', function () { setTimeout(render, 0); }, true);
    window.addEventListener('hashchange', function () { setTimeout(render, 0); });
    // links can change after a library scan: forget them when the user navigates elsewhere for a while
    setInterval(function () { cache = {}; }, 5 * 60 * 1000);
    render();

    window.PitchMeetings = { version: cfg.version, refresh: function () { cache = {}; removeSections(); render(); } };
})();
