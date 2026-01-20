extends Control

# Node references
@onready var player_hand_container: HBoxContainer = $MainLayout/PlayerArea/PlayerHand
@onready var opponent_hand_container: HBoxContainer = $MainLayout/OpponentArea/OpponentHand
@onready var table_cards_container: HBoxContainer = $MainLayout/TableArea/TableCards
@onready var log_label: Label = $MainLayout/LogPanel/LogScroll/LogLabel
@onready var score_label: Label = $MainLayout/TopBar/ScoreLabel
@onready var deck_info_label: Label = $MainLayout/TopBar/DeckInfo
@onready var turn_indicator: Label = $MainLayout/BottomBar/TurnIndicator
@onready var player_stats_label: Label = $MainLayout/PlayerArea/PlayerStats
@onready var opponent_stats_label: Label = $MainLayout/OpponentArea/OpponentStats
@onready var capture_hint_label: Label = $MainLayout/TableArea/CaptureHint
@onready var new_game_button: Button = $MainLayout/BottomBar/NewGameButton
@onready var capture_dialog: PanelContainer = $CaptureSelectionDialog
@onready var capture_options_container: VBoxContainer = $CaptureSelectionDialog/VBox/CaptureOptions
@onready var scopa_popup: PanelContainer = $ScopaPopup
@onready var round_end_dialog: PanelContainer = $RoundEndDialog
@onready var score_breakdown_label: Label = $RoundEndDialog/VBox/ScoreBreakdown
@onready var continue_button: Button = $RoundEndDialog/VBox/ContinueButton
@onready var game_over_dialog: PanelContainer = $GameOverDialog
@onready var winner_label: Label = $GameOverDialog/VBox/WinnerLabel
@onready var final_score_label: Label = $GameOverDialog/VBox/FinalScore
@onready var play_again_button: Button = $GameOverDialog/VBox/PlayAgainButton
@onready var ai_think_timer: Timer = $AIThinkTimer

# Preloads
const Card = preload("res://scripts/models/Card.gd")
const Player = preload("res://scripts/models/Player.gd")
const GameManager = preload("res://scripts/GameManager.gd")
const AIPlayer = preload("res://scripts/AIPlayer.gd")
const CardUIScene = preload("res://scenes/CardUI.tscn")

# Game state
var game_manager: GameManager
var ai_player: AIPlayer
var selected_hand_card: CardUI = null
var selected_table_cards: Array[CardUI] = []
var pending_capture_options: Array = []
var pending_card: Card = null
var log_messages: Array[String] = []
const MAX_LOG_LINES = 10

func _ready() -> void:
	# Initialize game systems
	game_manager = GameManager.new()
	ai_player = AIPlayer.new(AIPlayer.Difficulty.MEDIUM)
	add_child(game_manager)
	
	# Connect signals
	game_manager.game_state_changed.connect(_on_game_state_changed)
	game_manager.turn_changed.connect(_on_turn_changed)
	game_manager.scopa_scored.connect(_on_scopa_scored)
	game_manager.cards_captured.connect(_on_cards_captured)
	game_manager.round_ended.connect(_on_round_ended)
	game_manager.game_ended.connect(_on_game_ended)
	game_manager.log_message.connect(_on_log_message)
	
	new_game_button.pressed.connect(_on_new_game_pressed)
	continue_button.pressed.connect(_on_continue_pressed)
	play_again_button.pressed.connect(_on_new_game_pressed)
	ai_think_timer.timeout.connect(_on_ai_think_timeout)
	
	# Start game
	game_manager.start_new_game()
	add_log("Benvenuto a Scopa! Clicca una carta dalla tua mano per giocarla.")

func _on_game_state_changed() -> void:
	render_all()

func _on_turn_changed(current_player: Player) -> void:
	update_turn_indicator()
	
	# If it's AI's turn, trigger AI play after a delay
	if not current_player.is_human:
		set_player_cards_interactive(false)
		ai_think_timer.start(ai_player.think_delay())

func _on_ai_think_timeout() -> void:
	perform_ai_turn()

func perform_ai_turn() -> void:
	if game_manager.current_player.is_human:
		return
	
	var play = ai_player.choose_play(
		game_manager.player2,
		game_manager.table_cards,
		game_manager
	)
	
	if play.has("card") and play["card"] != null:
		game_manager.play_card(game_manager.player2, play["card"], play.get("capture", []))

func _on_scopa_scored(player: Player) -> void:
	show_scopa_popup()

func _on_cards_captured(player: Player, cards: Array) -> void:
	# Visual feedback could be added here
	pass

func _on_round_ended(p1_score: int, p2_score: int) -> void:
	show_round_end_dialog(p1_score, p2_score)

func _on_game_ended(winner: Player) -> void:
	show_game_over_dialog(winner)

func _on_log_message(message: String) -> void:
	add_log(message)

func _on_new_game_pressed() -> void:
	hide_all_dialogs()
	game_manager.start_new_game()
	clear_selection()

func _on_continue_pressed() -> void:
	round_end_dialog.visible = false
	if not game_manager.is_game_over():
		game_manager.start_new_round()

# ============ RENDERING ============

func render_all() -> void:
	render_player_hand()
	render_opponent_hand()
	render_table()
	update_scores()
	update_stats()
	update_deck_info()
	update_turn_indicator()

func render_player_hand() -> void:
	clear_container(player_hand_container)
	
	for card in game_manager.player1.hand:
		var card_ui = create_card_ui(card, false)
		card_ui.card_clicked.connect(_on_player_card_clicked)
		player_hand_container.add_child(card_ui)
	
	set_player_cards_interactive(game_manager.current_player == game_manager.player1)

func render_opponent_hand() -> void:
	clear_container(opponent_hand_container)
	
	for card in game_manager.player2.hand:
		var card_ui = create_card_ui(card, true)  # Face down
		card_ui.set_selectable(false)
		opponent_hand_container.add_child(card_ui)

func render_table() -> void:
	clear_container(table_cards_container)
	
	for card in game_manager.table_cards:
		var card_ui = create_card_ui(card, false)
		card_ui.card_clicked.connect(_on_table_card_clicked)
		table_cards_container.add_child(card_ui)

func create_card_ui(card: Card, face_down: bool) -> CardUI:
	var card_ui: CardUI
	
	# Try to instantiate from scene, fall back to code creation
	if CardUIScene:
		card_ui = CardUIScene.instantiate()
	else:
		card_ui = create_card_ui_fallback()
	
	card_ui.setup(card, face_down)
	return card_ui

func create_card_ui_fallback() -> CardUI:
	# Create a simple button-based card UI as fallback
	var btn = Button.new()
	btn.custom_minimum_size = Vector2(70, 100)
	# This won't have full CardUI functionality but will display
	return btn as CardUI

func clear_container(container: Container) -> void:
	for child in container.get_children():
		child.queue_free()

func set_player_cards_interactive(interactive: bool) -> void:
	for card_ui in player_hand_container.get_children():
		if card_ui is CardUI:
			card_ui.set_selectable(interactive)

# ============ PLAYER INPUT ============

func _on_player_card_clicked(card_ui: CardUI) -> void:
	if game_manager.current_player != game_manager.player1:
		add_log("Non è il tuo turno!")
		return
	
	var card = card_ui.get_card()
	var captures = game_manager.find_all_capture_combinations(card)
	
	# Deselect previous selection
	if selected_hand_card:
		selected_hand_card.set_selected(false)
	
	selected_hand_card = card_ui
	card_ui.set_selected(true)
	
	# Clear table selection
	clear_table_selection()
	
	if captures.size() == 0:
		# No captures possible - play card directly to table
		game_manager.play_card(game_manager.player1, card)
		clear_selection()
	elif captures.size() == 1:
		# Only one capture option - auto capture
		game_manager.play_card(game_manager.player1, card, captures[0])
		clear_selection()
	else:
		# Multiple capture options - let player choose
		show_capture_options(card, captures)

func _on_table_card_clicked(card_ui: CardUI) -> void:
	if selected_hand_card == null:
		add_log("Prima seleziona una carta dalla tua mano")
		return
	
	# Toggle selection
	if card_ui in selected_table_cards:
		selected_table_cards.erase(card_ui)
		card_ui.set_highlighted(false)
	else:
		selected_table_cards.append(card_ui)
		card_ui.set_highlighted(true)
	
	update_capture_hint()

func show_capture_options(card: Card, captures: Array) -> void:
	pending_card = card
	pending_capture_options = captures
	
	# Clear previous options
	for child in capture_options_container.get_children():
		child.queue_free()
	
	# Add option buttons
	for i in captures.size():
		var capture = captures[i]
		var btn = Button.new()
		var card_names = []
		for c in capture:
			card_names.append(c._to_string())
		btn.text = " + ".join(card_names)
		btn.pressed.connect(_on_capture_option_selected.bind(i))
		capture_options_container.add_child(btn)
	
	capture_dialog.visible = true

func _on_capture_option_selected(index: int) -> void:
	capture_dialog.visible = false
	
	if pending_card and index < pending_capture_options.size():
		game_manager.play_card(game_manager.player1, pending_card, pending_capture_options[index])
	
	clear_selection()

func clear_selection() -> void:
	if selected_hand_card:
		selected_hand_card.set_selected(false)
		selected_hand_card = null
	
	clear_table_selection()
	pending_card = null
	pending_capture_options.clear()
	capture_hint_label.text = ""

func clear_table_selection() -> void:
	for card_ui in selected_table_cards:
		if is_instance_valid(card_ui):
			card_ui.set_highlighted(false)
	selected_table_cards.clear()

func update_capture_hint() -> void:
	if selected_hand_card == null or selected_table_cards.size() == 0:
		capture_hint_label.text = ""
		return
	
	var total = 0
	for card_ui in selected_table_cards:
		total += card_ui.get_card().rank
	
	var hand_card_rank = selected_hand_card.get_card().rank
	
	if total == hand_card_rank:
		capture_hint_label.text = "✓ Cattura valida! Clicca la tua carta per confermare"
		capture_hint_label.modulate = Color.GREEN
	else:
		capture_hint_label.text = "Somma: %d (serve %d)" % [total, hand_card_rank]
		capture_hint_label.modulate = Color.YELLOW

# ============ UI UPDATES ============

func update_scores() -> void:
	score_label.text = "%s: %d - %s: %d" % [
		game_manager.player1.player_name,
		game_manager.player1_total_score,
		game_manager.player2.player_name,
		game_manager.player2_total_score
	]

func update_stats() -> void:
	player_stats_label.text = "Carte: %d | Scope: %d" % [
		game_manager.player1.get_captured_count(),
		game_manager.player1.scope_count
	]
	
	opponent_stats_label.text = "Carte: %d | Scope: %d" % [
		game_manager.player2.get_captured_count(),
		game_manager.player2.scope_count
	]

func update_deck_info() -> void:
	deck_info_label.text = "Mazzo: %d" % game_manager.get_deck_count()

func update_turn_indicator() -> void:
	if game_manager.current_player == game_manager.player1:
		turn_indicator.text = "⬤ Il tuo turno"
		turn_indicator.modulate = Color.GREEN
	else:
		turn_indicator.text = "◯ Turno del computer..."
		turn_indicator.modulate = Color.GRAY

func add_log(message: String) -> void:
	log_messages.append(message)
	if log_messages.size() > MAX_LOG_LINES:
		log_messages.pop_front()
	
	log_label.text = "\n".join(log_messages)

# ============ DIALOGS ============

func show_scopa_popup() -> void:
	scopa_popup.visible = true
	# Auto-hide after 1 second
	await get_tree().create_timer(1.0).timeout
	scopa_popup.visible = false

func show_round_end_dialog(p1_score: int, p2_score: int) -> void:
	var breakdown_text = "Questa mano:\n"
	breakdown_text += "%s: +%d punti\n" % [game_manager.player1.player_name, p1_score]
	breakdown_text += "%s: +%d punti\n\n" % [game_manager.player2.player_name, p2_score]
	breakdown_text += "Totale:\n"
	breakdown_text += "%s: %d\n" % [game_manager.player1.player_name, game_manager.player1_total_score]
	breakdown_text += "%s: %d" % [game_manager.player2.player_name, game_manager.player2_total_score]
	
	score_breakdown_label.text = breakdown_text
	round_end_dialog.visible = true

func show_game_over_dialog(winner: Player) -> void:
	if winner == game_manager.player1:
		winner_label.text = "🎉 HAI VINTO! 🎉"
		winner_label.modulate = Color.GOLD
	else:
		winner_label.text = "Hai perso..."
		winner_label.modulate = Color.GRAY
	
	final_score_label.text = "%d - %d" % [
		game_manager.player1_total_score,
		game_manager.player2_total_score
	]
	
	game_over_dialog.visible = true

func hide_all_dialogs() -> void:
	capture_dialog.visible = false
	scopa_popup.visible = false
	round_end_dialog.visible = false
	game_over_dialog.visible = false
