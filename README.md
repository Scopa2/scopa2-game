# Scopa 2 - Gioco di Carte Italiano

Un gioco di Scopa completo sviluppato in Godot 4.5, con modalità 1v1 contro il computer.

## 🎮 Come Giocare

### Avvio
Apri il progetto in Godot 4.5 e premi F5 per avviare il gioco.

### Controlli
- **Clicca** su una carta dalla tua mano per giocarla
- Se ci sono più opzioni di cattura, apparirà un menu per scegliere
- Il gioco passa automaticamente al turno del computer

## 📜 Regole della Scopa

### Setup
- Mazzo di **40 carte** (semi italiani: Denari, Coppe, Spade, Bastoni)
- **4 carte** vengono messe sul tavolo all'inizio
- Ogni giocatore riceve **3 carte** alla volta

### Cattura delle Carte
È possibile catturare carte dal tavolo in due modi:
1. **Cattura diretta**: Una carta in mano con lo stesso valore di una carta sul tavolo
2. **Cattura per somma**: Una carta in mano il cui valore è uguale alla somma di 2+ carte sul tavolo

Se non è possibile catturare, la carta viene posata sul tavolo.

### Scopa!
Si fa **Scopa** quando si catturano tutte le carte rimaste sul tavolo (eccetto l'ultima mano).

### Fine Mano
Quando terminano le carte in mano, vengono distribuite altre 3 carte fino all'esaurimento del mazzo.
Le carte rimaste sul tavolo vanno all'ultimo giocatore che ha catturato.

## 🏆 Punteggio

Alla fine di ogni mano si calcolano i punti:

| Punto | Descrizione |
|-------|-------------|
| **Carte** | 1 punto a chi ha più carte (21+) |
| **Denari** | 1 punto a chi ha più carte di Denari (6+) |
| **Sette Bello** | 1 punto a chi ha il 7 di Denari |
| **Primiera** | 1 punto a chi ha il punteggio primiera più alto |
| **Scope** | 1 punto per ogni scopa fatta |

### Primiera
Il punteggio primiera si calcola prendendo la carta migliore di ogni seme:
- 7 = 21 punti
- 6 = 18 punti  
- Asso = 16 punti
- 5 = 15 punti
- 4 = 14 punti
- 3 = 13 punti
- 2 = 12 punti
- Figure (8,9,10) = 10 punti

Vince la primiera chi ha il totale più alto (avendo almeno una carta per seme).

### Vittoria
Il primo giocatore a raggiungere **11 punti** vince la partita!

## 🤖 Intelligenza Artificiale

Il computer usa una strategia di livello medio che:
- Preferisce catturare più carte possibili
- Dà priorità alle carte di Denari e ai 7
- Cerca di fare Scopa quando possibile
- Evita di lasciare situazioni vantaggiose all'avversario

## 📁 Struttura del Progetto

```
├── assets/textures/deck/    # Sprite sheet delle carte
├── scenes/
│   ├── main.tscn           # Scena principale del gioco
│   └── CardUI.tscn         # Scena per la UI delle carte
├── scripts/
│   ├── main.gd             # Controller principale del gioco
│   ├── GameManager.gd      # Logica del gioco e regole
│   ├── AIPlayer.gd         # Intelligenza artificiale
│   ├── models/
│   │   ├── Card.gd         # Classe carta
│   │   └── Player.gd       # Classe giocatore
│   └── ui/
│       └── CardUI.gd       # UI della carta
```

## 🔧 Constraints Originali
Il gioco segue le regole della Scopa Napoletata.
La manche inizia con un mazzo di 40 carte, vengono messe 4 carte a terra e vengono date ogni turno 3 carte per giocatore. 

È possibile prendere le carte da terra in due modi:
- Se si ha una carta in mano di valore uguale ad una carta a terra (esempio 10 di coppe in mano e 10 di spade a terra)
- Se si ha una carta in mano di valore uguale alla somma di 2 o più carte a terra (esempio 10 di coppe in mano e a terra: 7 di spade e 3 di denari)

Se non è possibile prendere da terra, si mette una carta a terra tra quelle in mano.

È possibile fare scopa prendendo l'ultima o le ultime carte rimaste a terra.

Quando i giocatori finiscono le carte in mano, finisce il turno e vengono date di nuovo 3 carte a testa, fino alla fine delle carte.

Quando la partita finisce si contano le carte guadagnate e si possono fare i seguenti punti:
- Denari: 1 punto per il giocatore con più carte di denari
- 7 Bello: 1 punto per aver preso il 7 di denari
- Primiera: 1 punto per il giocatore con più 7 in mano
- Allungo: 1 punto per chi ha preso più carte
- Scopa: 1 punto per ogni scopa fatta

Vengono fatte nuove partite finchè uno dei due giocatori non arriva a 21.

## Modifiers (Santi)
È possibile acquista dei santini nello shop, questi hanno dei poteri che ti permettono di influenzare la partita:

- Vedere le prossime 2 carte ogni volta che peschi
- Cambia il seme di una carta tra quelle prese in denari
- Scambia la carta con quella dell'avversario
- Cambia il seme di una carta a terra in denari
- Cambia il seme di una carta in mano in denari
- Miracolo di San Gennaro (sciogli tutto il sangue nella fiala)
- Ti permette di selezionare delle carte da bruciare per ottenere in cambio il valore della carta +1 in gocce di sangue
- Vedere la mano dell'avversario (Santa Lucia)
- Rerolla la mano
- Brucia una carta da terra
- Scegli una briscola che fa assumere valore +3 alle carte di quel seme
- Scudo protettivo contro gli attacchi sulla tua mano (si attiva automaticamente quando avviene il primo attacco)
- Fai diventare la mano dell'avversario tutta di bastoni
- Tutta la mano diventa d'oro
- Tutta la terra diventa d'oro
- Piazza una carta casuale a terra

I santi possono avere tre rarità:
- Comune:
- Raro
- Leggendario
- Unico

Alcuni modificatori possono alterare anche le manche successive a quella attuale, ad esempio modificando tutti i prossimi mazzi.

## Shop condiviso

> Capire quando far usare lo shop

Lo shop mostra 3 santi disponibili per l'acquisto. Ogni santo ha un indicatore che mostra per quanti turni rimarrà ancora disponibile per l'acquisto. I giocatori vedono esattamente lo stesso shop. Il primo che compra un santo lo rimuove istantaneamente e viene rimpiazzato con un altro. Se dovesse scadere il timer sul santo verrà rimpiazzato con un altro. La probabilità di far apparire santi rari è minore di quella dei santi comuni.

## Come comprare dallo shop
Quando selezioni un modifier ti verrà chiesto di sacrificare delle carte tra quelle che hai guadagnato per poterle usare come metodo di pagamento.

Valori delle carte (seguono la logica di briscola):
- 2, 4, 5, 6, 7: 0
- 8: 2
- 9: 3
- 10: 4
- 3: 10
- 1: 11

Valori dei semi:
- Bastoni: +0
- Spade: +1
- Coppe +2
- Denari +3

Se per il pagamento dovesse esserci del resto, questo verrà salvato in una fiala di gocce di sangue di san gennaro.  (Se non usi il resto entro 1 turno, il sangue si solidifica)

## Punti in sospeso
### Territorio di gioco
- Vesuvio
- Mergellina
- Campi Flegrei
### Quando piazzare lo shop?
### Far vedere o no le carte prese dall'avversario? $\rightarrow$ Ci sono modificatori per cambiare il valore delle carte prese? (es 2 diventa 10)?