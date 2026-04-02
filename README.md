Ecco il documento di progetto aggiornato. Ho integrato le regole originali (che sono rimaste invariate) con le specifiche tecniche dell'architettura distribuita che abbiamo definito (Laravel, Event Sourcing, PGN, WebSockets).

---

# Scopa 2 - Progetto di Sistemi Distribuiti

Un'implementazione moderna del gioco della Scopa, sviluppata con un'architettura **Client-Server** che garantisce consistenza dello stato, persistenza degli eventi e interazione in tempo reale.

* **Client:** Godot Engine 4.5 (GDScript)
* **Server:** Laravel 10+ (PHP)
* **Real-time:** Laravel Reverb / Soketi (WebSockets)
* **Database:** MySQL / PostgreSQL (Event Store)

---

## 1. 🏗 Architettura del Sistema

Il progetto adotta un pattern **Event Sourcing** con esecuzione deterministica. Il server non salva solo lo stato finale, ma la sequenza immutabile di azioni che ha portato a quello stato.

### Principi Chiave (Sistemi Distribuiti)

1. **Single Source of Truth (SSOT):** Il server Laravel detiene lo stato autoritativo. Il client è solo una "proiezione" visiva dello stato.
2. **Determinismo:** Ogni partita nasce da un **Seed** crittografico. Dato lo stesso Seed e la stessa sequenza di mosse (PGN), lo stato finale sarà sempre identico (mazzo, shop, pescate).
3. **Concurrency Control:** Utilizzo di transazioni atomiche e locking sul database per gestire **Race Conditions** (es. due giocatori che comprano lo stesso oggetto nello shop simultaneamente).
4. **Information Hiding:** Il server invia al client solo le informazioni che il giocatore *può* vedere (mascheramento delle carte avversarie e del mazzo).

### Flusso Dati

1. **Azione:** Il Client invia una richiesta HTTP POST (micro-azione).
2. **Validazione:** Il Server ricostruisce lo stato attuale riproducendo gli eventi passati e valida la nuova mossa.
3. **Persistenza:** Se valida, la mossa viene salvata come evento immutabile nel DB.
4. **Broadcast:** Il nuovo stato viene calcolato e inviato via WebSocket a tutti i client connessi.

---

## 2. 📜 Regole di Gioco (Core)

### Setup

* Mazzo di **40 carte** (Denari, Coppe, Spade, Bastoni).
* **4 carte** sul tavolo all'inizio.
* **3 carte** distribuite a ogni giocatore per turno.

### Meccaniche di Presa

1. **Cattura diretta**: Carta in mano = Carta sul tavolo (es. 10 prende 10).
2. **Cattura per somma**: Carta in mano = Somma di carte sul tavolo (es. Re prende 3 + 7).
3. **Scarto**: Se non si può catturare, la carta viene lasciata a terra.
4. **Scopa**: Prendere tutte le carte a terra vale 1 punto (segnalato nel log).

### Punteggio (Fine Manche)

Si vince a **11 punti**.
| Punto | Descrizione |
|-------|-------------|
| **Carte** | 1pt (chi ha 21+ carte) |
| **Denari** | 1pt (chi ha 6+ carte denari) |
| **7 Bello** | 1pt (chi ha il 7 di Denari) |
| **Primiera** | 1pt (punteggio calcolato sui 7, 6, Asso, ecc.) |
| **Scope** | 1pt per ogni scopa effettuata |

---

## 3. 🔮 Modifiers (Santi) & Shop Condiviso

Oltre alle regole base, i giocatori possono acquistare potenziamenti.

### Logica dello Shop

* Lo shop è **condiviso** e aggiornato in tempo reale.
* Contiene 3 "Santi" con rarità variabile.
* Le azioni sono atomiche: "Chi prima clicca, prima compra".

### Valuta di Scambio (Sacrificio)

Per comprare un Santo, si sacrificano carte dal mazzo delle proprie *carte prese*. Il valore è basato sulla **Briscola**:

* **Asso (1):** 11 punti
* **Tre (3):** 10 punti
* **Re (10):** 4 punti
* **Cavallo (9):** 3 punti
* **Fante (8):** 2 punti
* **Altri (2,4,5,6,7):** 0 punti

**Bonus Semi:** Denari (+3), Coppe (+2), Spade (+1), Bastoni (+0).

### Sangue di San Gennaro

Se il valore delle carte sacrificate supera il costo del Santo, il resto viene convertito in "Gocce di Sangue" (accumulabili per 1 turno) che possono essere usate per acquisti futuri.

### Lista Santi Implementati

* **San Gennaro:** Scioglie il sangue / Scambia carta con avversario.
* **Santa Lucia:** Vedi le carte dell'avversario (toglie il mascheramento dal payload JSON).
* **Sant'Antonio:** Brucia una carta da terra.
* **Mida:** Trasforma una carta in Denari (Oro).
* ... (altri come da specifica originale)

---

## 4. 🔌 Protocollo di Comunicazione (API & PGN)

### Endpoints REST

| Metodo | Endpoint | Descrizione |
| --- | --- | --- |
| `POST` | `/api/games` | Crea partita (ritorna `game_id` e `seed`). |
| `GET` | `/api/games/{id}` | Scarica lo stato attuale (Sync). |
| `POST` | `/api/games/{id}/action` | Invia una mossa (Compra, Usa, Gioca). |

### Notation Protocol (Scopa Algebraic Notation)

Per salvare lo storico e garantire l'audit, usiamo una notazione testuale compatta ispirata agli scacchi, salvata nel DB.

**Grammatica:**

* **Carte:** `7D` (7 Denari), `10C` (Re Coppe).
* **Presa:** `x` (es. `7Dx7S` o `10Cx(3D+7B)`).
* **Scopa:** `#` a fine mossa.
* **Shop:** `$` seguito da ID e pagamento (es. `$GEN(1C)`).
* **Abilità:** `@` seguito da ID e target (es. `@LUC[P2]`).

**Esempio di Log Partita:**

```text
1. 4S           $LUC(1C) 5D  ; P1 scarta. P2 compra Lucia e scarta.
2. 7Dx(3S+4S)   10Sx10D#     ; P1 prende. P2 fa Scopa.
3. @LUC         ...          ; P1 attiva Lucia (vede carte P2).

```

### Struttura JSON di Ritorno (Stato)

Il payload inviato via WebSocket dopo ogni mossa:

```json
{
  "game_id": "uuid-...",
  "turn": "p2",
  "table": ["3D", "5S", "1B"],
  "shop": ["GEN", "ANT"],
  "deck_count": 24,
  "players": {
    "p1": {
      "hand": ["7D", "2C", "1S"], // Visibile solo a P1
      "captured_count": 4,
      "santi": ["GEN"],
      "blood": 2
    },
    "p2": {
      "hand": ["BACK", "BACK", "BACK"], // Oscurato per P1
      "captured_count": 2,
      "santi": [],
      "blood": 0
    }
  }
}

```

---

## 5. 📁 Struttura del Progetto

Il progetto è diviso in due repository logiche.

### Backend (Laravel)

```
app/
├── GameEngine/             # Core Logic (Agnostico dal Framework)
│   ├── GameState.php       # DTO dello stato (Marshaling dati)
│   ├── ScopaEngine.php     # State Machine (Replay Eventi)
│   ├── ScopaNotation.php   # Parser PGN
│   └── GameConstants.php   # Regole e Valori
├── Http/Controllers/
│   └── GameController.php  # API Entry point
├── Models/
│   ├── Game.php            # Tabella partite (Seed, Status)
│   └── GameEvent.php       # Tabella eventi (Append-only Log)
└── Events/
    └── GameStateUpdated.php # Evento Broadcast WebSocket

```

### Frontend (Godot)

```
res://
├── scenes/
│   ├── MainGame.tscn       # Scena di gioco
│   ├── Card.tscn           # Oggetto Carta interattivo
│   └── ShopOverlay.tscn    # UI Shop
├── scripts/
│   ├── NetworkManager.gd   # Gestione HTTP e WebSocket
│   ├── GameState.gd        # Parsing del JSON ricevuto
│   └── controllers/
│       ├── InputController.gd # Gestione click e drag
│       └── FXManager.gd       # Animazioni (Sangue, Scope)
└── assets/                 # Texture Napoletane

```

---

## 6. 🧠 Logica di Sincronizzazione Client

Il client Godot è "stupido" (Thin Client) per quanto riguarda le regole, ma "intelligente" per le animazioni.

1. **Input:** Utente trascina carta.
2. **Lock UI:** L'interfaccia si blocca o mostra un loader sottile.
3. **Request:** Invia l'azione al server.
4. **Update:**
* Se riceve `200 OK` + JSON: Aggiorna la posizione delle carte interpolando (tweening) dalla posizione attuale a quella nuova dettata dal server.
* Se riceve `422 Error`: Mostra "Mossa non valida" e riporta la carta in mano.



### Gestione Target dei Santi

Quando si attiva un Santo che richiede target (es. "Scambia carta"):

1. Il Client apre un **Picker UI** (Selettore).
2. L'utente seleziona le carte visivamente.
3. Il Client costruisce la stringa PGN: `@SWP[MY_CARD|OPP_CARD]`.
4. Il Server valida che le carte siano effettivamente possedute dai rispettivi giocatori.

---

## 7. Punti Aperti & Note Sviluppo

* **Territori:** Vesuvio, Mergellina (Solo grafici o influenzano il gameplay? Da decidere).
* **Animazione Carte:** Implementare un buffer in Godot per gestire la coda di eventi se ne arrivano due molto vicini (es. avversario compra e gioca subito).
* **Riconnessione:** Se il WebSocket cade, il client deve chiamare `GET /api/games/{id}` per risincronizzarsi.

---

## 8. 🚀 Releases & Distribuzione

### Creazione di una Release

Il progetto utilizza GitHub Actions per automatizzare la compilazione e distribuzione del client Godot su tutte le piattaforme.

#### Come Pubblicare una Release

1. **Assicurati che le tue modifiche siano committed e pushed su GitHub:**
   ```bash
   git add .
   git commit -m "Descrizione delle modifiche"
   git push origin main
   ```

2. **Crea e pusha un tag con versione semantica:**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

3. **El workflow si avvia automaticamente** e compila il gioco per:
   - **Windows** (.exe con DLL, x86_64)
   - **macOS** (.app bundle universal - Intel + Apple Silicon)
   - **Linux x86_64** (eseguibile .x86_64)
   - **Linux ARM64** (eseguibile .arm64 per Raspberry Pi, ARM servers)

4. **Dopo 5-15 minuti**, la release sarà pubblicata automaticamente su GitHub con tutti i build allegati come file ZIP.

#### Versioning Guidelines

Utilizziamo **Semantic Versioning** (MAJOR.MINOR.PATCH):

- **MAJOR (v2.0.0):** Modifiche incompatibili o riscritture significative
- **MINOR (v1.1.0):** Nuove funzionalità compatibili con le versioni precedenti
- **PATCH (v1.0.1):** Bug fix e piccole correzioni

#### Dove Trovare le Release

Le release pubblicate sono disponibili nella sezione [Releases](../../releases) del repository GitHub. Ogni release include:
- **Scopa2-Windows.zip** - Windows 10/11 (x86_64)
- **Scopa2-macOS.zip** - macOS 10.13+ Universal (Intel + Apple Silicon)
- **Scopa2-Linux.zip** - Linux (x86_64)
- **Scopa2-Linux-ARM64.zip** - Linux ARM64 (Raspberry Pi 4+, ARM servers)
- Note di rilascio auto-generate dai commit
- Tag della versione

#### Piattaforme Supportate

| Piattaforma | Architettura | Note |
|-------------|--------------|------|
| Windows | x86_64 | Windows 10/11 |
| macOS | Universal (x86_64 + ARM64) | macOS 10.13+, Intel e Apple Silicon (M1/M2/M3) |
| Linux | x86_64 | Distro moderne con glibc 2.31+ |
| Linux | ARM64 | Raspberry Pi 4+, server ARM |

#### Build Details

- **Runtime .NET**: Embedded (gli utenti non devono installare .NET)
- **Debug Symbols**: Esclusi (build ottimizzate per produzione)
- **Godot Version**: 4.6 con supporto Mono/C#

#### Troubleshooting

Se il workflow di release fallisce:
1. Controlla i log su GitHub Actions nella tab "Actions"
2. Verifica che `export_presets.cfg` sia presente e configurato correttamente
3. Assicurati che il progetto compili localmente con Godot 4.6

Per testare il workflow senza creare una release ufficiale, usa un tag di pre-release:
```bash
git tag v0.1.0-test
git push origin v0.1.0-test
```