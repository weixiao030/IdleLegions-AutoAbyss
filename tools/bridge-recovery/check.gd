extends Node

class FakeGame extends Node:
	var state: Dictionary = {"inventory": [{"id": "existing"}]}
	var content: Dictionary = {}

class FakeNet extends Node:
	signal _response_arrived
	var _next_id: int = 1
	var _results: Dictionary = {}
	var reads: int = 0
	var latest: Dictionary = {}
	func is_open() -> bool:
		return true
	func request(_method: String, _args: Dictionary) -> Dictionary:
		reads += 1
		await get_tree().create_timer(0.4).timeout
		return {"ok": true, "data": latest.duplicate(true)}

class FakePanel extends Node:
	var _data: Dictionary = {}
	var _req_inflight: bool = false
	var _omen_ev: Dictionary = {}
	var bridge: Node
	var payload: Dictionary = {}
	var reply_ok: bool = true
	var omit_receipt: bool = false
	func _pide(_method: String, _args: Dictionary) -> void:
		var id: int = bridge.net._next_id
		bridge.net._next_id += 1
		await get_tree().create_timer(0.02).timeout
		if not omit_receipt:
			bridge.net._results[id] = {"ok": reply_ok, "data": payload, "error": "" if reply_ok else "fixture refusal"}
			bridge.net._response_arrived.emit()
			bridge.net._results.erase(id)
	func _pelea(method: String, args: Dictionary) -> void:
		await _pide(method, args)

var checks: int = 0
func check(ok: bool, label: String) -> void:
	if not ok:
		push_error(label)
		get_tree().quit(1)
		assert(ok, label)
	checks += 1
	print("CHECK: " + label)

func _ready() -> void:
	var script: Script = load("res://test_bridge.gd")
	check(script != null, "Bridge parses")
	var bridge: Node = script.new()
	get_tree().root.add_child.call_deferred(bridge)
	var game: FakeGame = FakeGame.new()
	game.name = "Game"
	get_tree().root.add_child.call_deferred(game)
	var net: FakeNet = FakeNet.new()
	net.name = "Net"
	get_tree().root.add_child.call_deferred(net)
	await get_tree().process_frame
	bridge.game = game
	bridge.net = net
	net._response_arrived.connect(bridge._capture)
	var panel: FakePanel = FakePanel.new()
	add_child(panel)
	panel.bridge = bridge
	bridge.panel = panel
	bridge.sima = {"run": {"id": "fixture-run", "nodo": "fixture-boss", "hechos": []}}
	panel._data = bridge.sima.duplicate(true)
	net.latest = bridge.sima.duplicate(true)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(bridge.DIR))
	var key: FileAccess = FileAccess.open(bridge.DIR + "/session.key", FileAccess.WRITE)
	key.store_string("fixture-key-000000000000000000000000000000")
	key.close()
	var cmd: Dictionary = {"id": "action-a", "key": "fixture-key-000000000000000000000000000000", "method": "sima.fight", "args": {}, "issuedAt": Time.get_unix_time_from_system(), "runId": "fixture-run", "nodeId": "fixture-boss"}
	panel.payload = {"result": {"win": true, "events": []}}
	bridge._save("command.json", cmd)
	bridge._tick()
	await get_tree().create_timer(0.12).timeout
	var snap: Variant = JSON.parse_string(FileAccess.get_file_as_string(bridge.DIR + "/snapshot.json"))
	check(snap is Dictionary and snap.get("lastCommand", {}).get("id") == "action-a", "Post-command snapshot is published before an unrelated network refresh")
	check(net.reads == 0, "Completed action does not immediately issue a redundant sima.get")
	check(snap.lastAction.id == "action-a" and snap.lastAction.confirmed and snap.lastAction.ok, "Successful action receipt is retained with its command ID")
	check(snap.lastBattle.commandId == "action-a", "Battle receipt carries its exact command ID")
	check(snap.lastBattle.before.id == "fixture-run" and snap.lastBattle.before.nodo == "fixture-boss", "Battle receipt preserves run and room attribution")
	check(bridge.receipt.ok and net._results.is_empty(), "Receipt is captured before Net consumes its response dictionary")
	cmd.id = "refresh-a"
	cmd.method = "local.refresh"
	bridge._save("command.json", cmd)
	await bridge._tick()
	snap = JSON.parse_string(FileAccess.get_file_as_string(bridge.DIR + "/snapshot.json"))
	check(snap.lastCommand.id == "refresh-a" and snap.lastAction.id == "action-a", "Read-only refresh cannot erase the last action receipt")
	check(net.reads == 1, "Explicit refresh is performed once")
	cmd.id = "action-b"
	cmd.method = "sima.fight"
	panel.reply_ok = false
	panel.payload = {}
	bridge._save("command.json", cmd)
	await bridge._tick()
	snap = JSON.parse_string(FileAccess.get_file_as_string(bridge.DIR + "/snapshot.json"))
	check(snap.lastAction.id == "action-b" and not snap.lastAction.ok, "A refused command cannot inherit an old successful receipt")
	check(snap.lastAction.error == "fixture refusal", "Real refusal reason is preserved")
	cmd.id = "action-c"
	panel.omit_receipt = true
	bridge._save("command.json", cmd)
	await bridge._tick()
	snap = JSON.parse_string(FileAccess.get_file_as_string(bridge.DIR + "/snapshot.json"))
	check(not snap.lastAction.confirmed and snap.lastAction.code == "receipt_missing", "Missing receipt remains unknown instead of being fabricated as success")
	check(not snap.lastAction.retryable, "An already-dispatched command is never marked safe to replay")
	game.free()
	net.free()
	panel.free()
	bridge.free()
	print("BRIDGE_RECOVERY_OK: %d checks" % checks)
	get_tree().quit(0)
