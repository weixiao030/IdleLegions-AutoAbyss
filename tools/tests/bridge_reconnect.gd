extends Node

const BridgeScript = preload("res://bridge.gd")
var checks: int = 0

class FakeGame extends Node:
	var token: String = "fixture-secret"
	var state: Dictionary = {"fixture": true}
	var applied: int = 0
	func apply_login(data: Dictionary) -> void:
		applied += 1
		token = data.token
		state = data.state

class FakeNet extends Node:
	var socket: Variant = RefCounted.new()
	var opened: bool = true
	var replies: Array = []
	var methods: Array = []
	var swap_on_request: bool = false
	func is_open() -> bool:
		return opened
	func request(method: String, _args: Dictionary) -> Dictionary:
		methods.append(method)
		if swap_on_request:
			socket = RefCounted.new()
		return replies.pop_front() if not replies.is_empty() else {"ok": false, "error": "请先登录"}

func check(ok: bool, label: String) -> void:
	if not ok:
		push_error("FAILED: " + label)
		get_tree().quit(1)
		assert(ok, label)
	checks += 1
	print("PASS ", label)

func fixture() -> Node:
	var bridge: Node = BridgeScript.new()
	bridge.set_process(false)
	add_child(bridge)
	bridge.game = FakeGame.new()
	bridge.net = FakeNet.new()
	add_child(bridge.game)
	add_child(bridge.net)
	return bridge

func good() -> Dictionary:
	return {"ok": true, "data": {"run": {"id": "same-run", "nodo": "same-room"}, "cuerdas": 15}}

func login_good() -> Dictionary:
	return {"ok": true, "data": {"token": "rotated-fixture-secret", "state": {"fixture": true}}}

func _ready() -> void:
	var b: Node = fixture()
	b.net.replies = [good()]
	var r: Dictionary = await b._refresh_authenticated()
	check(r.ok and b._server_logged_in() and b.net.methods == ["sima.get"], "normal read does not authenticate")
	b.peeks = {"known-room": {"fixture": true}}
	b.net.replies = [good()]
	r = await b._refresh_authenticated()
	check(r.ok and b.peeks.has("known-room"), "periodic reads preserve same-connection enemy cache")
	b.net.replies = [good()]
	r = await b._command({"method": "local.refresh", "issuedAt": Time.get_unix_time_from_system()})
	check(r.ok and b.peeks.is_empty(), "explicit reconciliation invalidates cached enemy previews")
	b.net.socket = RefCounted.new()
	check(not b._server_logged_in(), "socket replacement invalidates cached login immediately")
	b.peeks = {"old-room": {"fixture": true}}
	b.net.replies = [{"ok": false, "error": "请先登录"}, login_good(), good()]
	b.net.methods.clear()
	r = await b._refresh_authenticated()
	check(r.ok and b.game.applied == 1 and b._server_logged_in() and b.peeks.is_empty(), "official login restores authenticated state")
	check(b.net.methods == ["sima.get", "auth.token", "sima.get"], "recovery never replays fight or resource actions")
	check(b.sima.run.id == "same-run" and b.sima.run.nodo == "same-room" and b.sima.cuerdas == 15, "recovery uses newly queried same-run state")
	b.net.replies = [{"ok": false, "error": "请先登录"}]
	b.net.methods.clear()
	r = await b._refresh_authenticated()
	check(not r.ok and not b._server_logged_in() and b.net.methods == ["sima.get"], "same socket cannot enter authentication loop")
	b.net.socket = RefCounted.new()
	b.net.replies = [{"ok": false, "code": "auth_required"}, login_good(), good()]
	b.net.methods.clear()
	r = await b._refresh_authenticated()
	check(r.ok and b.net.methods == ["sima.get", "auth.token", "sima.get"], "new socket permits one official login attempt")
	b = fixture()
	b.game.token = ""
	r = await b._refresh_authenticated()
	check(not r.ok and b.net.methods == ["sima.get"] and b.game.applied == 0, "empty token requires normal login")
	b = fixture()
	b.net.replies = [{"ok": false, "error": "business rule refused"}]
	r = await b._refresh_authenticated()
	check(not r.ok and b.net.methods == ["sima.get"] and b.game.applied == 0, "business failure never authenticates")
	b = fixture()
	b.net.opened = false
	r = await b._refresh_authenticated()
	check(not r.ok and b.net.methods.is_empty() and not b._server_logged_in(), "closed connection sends nothing")
	b = fixture()
	b.net.replies = [{"ok": false, "error": "请先登录"}, {"ok": false, "error": "fixture-secret"}]
	r = await b._refresh_authenticated()
	check(not r.ok and b.game.applied == 0 and not JSON.stringify(r).contains("fixture-secret"), "failed auth leaves game intact and hides credential echoes")
	b = fixture()
	b.net.replies = [{"ok": false, "error": "请先登录"}, {"ok": true, "data": {"state": {}}}]
	r = await b._refresh_authenticated()
	check(not r.ok and b.game.applied == 0, "malformed login cannot overwrite account state")
	b = fixture()
	b.net.replies = [good()]
	b.net.swap_on_request = true
	r = await b._refresh_authenticated()
	check(not r.ok and not b._server_logged_in() and b.sima.is_empty(), "old socket reply cannot authenticate new connection")
	b = fixture()
	var redacted: Variant = b._clean({"TOKEN": "secret-a", "nested": [{"password": "secret-b", "ticket": "secret-c"}], "safe": 7})
	check(not JSON.stringify(redacted).contains("secret-") and redacted.safe == 7, "recursive credential redaction remains active")
	var cmd: Dictionary = {"method": "sima.fight", "issuedAt": Time.get_unix_time_from_system()}
	r = await b._command(cmd)
	check(not r.ok and r.retryable and b.net.methods.is_empty(), "unauthenticated action is proven unsent")
	# Test the actual persisted snapshot, not just the predicate.
	b._snapshot()
	var snap: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(b.DIR + "/snapshot.json"))
	check(not snap.loggedIn and snap.version == "0.3.7", "snapshot rejects stale cached account login")
	print("BRIDGE_RECONNECT_TESTS_COMPLETE ", checks)
	get_tree().quit()
