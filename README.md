# Pitch Meetings – Jellyfin-Plugin

Verknüpft die Pitch-Meeting-Videos aus der lokalen YouTube-Bibliothek automatisch mit den Filmen und Serien, um die es geht, und zwar in beide Richtungen:

- **Filmseite / Serienseite / Staffelseite:** Abschnitt „Pitch Meeting“ mit Vorschaubild. Klick öffnet das Video, der Play-Button spielt es direkt ab.
- **Pitch-Meeting-Seite:** Abschnitt „Zum Film“ bzw. „Zur Serie“ / „Zur Staffel“ mit dem Poster.

Der Abschnitt nutzt Jellyfins eigene Zeilen- und Karten-Markup und wird von Themes deshalb wie jede andere Reihe gestylt.

## So funktioniert die Zuordnung

Grundlage ist der Episodentitel des Videos, z. B. „The Fantastic Four - First Steps“.

1. Zusätze wie „Pitch Meeting“, „Classic Pitch Meeting“ oder „| Ryan George“ werden entfernt.
2. Eine Jahreszahl in Klammern („Street Fighter (1994)“) wählt die passende Fassung.
3. „… Season 5“ verknüpft mit Staffel 5 der Serie, falls vorhanden, sonst mit der Serie.
4. Die Titel werden unabhängig von Schreibweise verglichen: Groß-/Kleinschreibung, Satzzeichen, Doppelpunkt/Bindestrich, „&“/„and“, römische Zahlen, Akzente und führendes „The“ spielen keine Rolle. Verglichen wird mit **Titel und Originaltitel** (deutsch betitelter Film „Die Eiskönigin“ wird über „Frozen“ gefunden).
5. Vorangestellte Namen wie „Lee Cronin's The Mummy“ werden notfalls auch ohne den Namen gesucht.
6. Gibt es mehrere gleichnamige Filme ohne Jahreshinweis, gewinnt der neueste, der beim Hochladen des Videos schon erschienen war (Erscheinungsdatum des Videos = Episoden-Datum).
7. Liegt derselbe Film mehrfach vor (z. B. 4K- und HD-Bibliothek), zeigen alle Kopien das Pitch Meeting.

Nach jedem Bibliotheks-Scan wird automatisch neu abgeglichen. Neue Videos und neue Filme verknüpfen sich also von selbst.

## Einstellungen (Dashboard → Plugins → Pitch Meetings)

| Einstellung | Standard | Bedeutung |
|---|---|---|
| Show the links in the web client | an | Hauptschalter |
| Series with the Pitch Meeting videos | `Pitch Meetings` | Name der Serie in der YouTube-Bibliothek, mehrere durch Komma getrennt |
| Link Pitch Meetings about TV shows | an | „… Season N“ mit Serien/Staffeln verknüpfen |
| Movie and show pages | an | Abschnitt auf Film-/Serienseiten |
| Pitch Meeting pages | an | Abschnitt auf der Seite des Videos |

Darunter steht die Liste der Videos (standardmäßig nur die ohne Verknüpfung). Dort lässt sich jedes Video von Hand einem Film, einer Serie oder Staffel zuordnen oder mit „— no link —“ ausschließen. Leeres Feld = wieder automatisch. Korrekturen werden mit **Save** übernommen.

## Installation

1. Ordner `dist\PitchMeetings_1.0.0.0` in das Plugin-Verzeichnis von Jellyfin kopieren.
2. Jellyfin neu starten.
3. Im Browser Strg+F5. In der Konsole liefert `PitchMeetings.version` die geladene Version.

## Bauen

```powershell
.\build.ps1
```
