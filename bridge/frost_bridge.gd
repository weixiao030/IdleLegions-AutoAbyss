extends Node
## Original local companion bridge. No executable game files are changed.
const DIR: String = "user://frost_abyss"
const CFG: String = "user://ventana_inicio.cfg"
const LINE: String = "FrostAbyss=\"*user://frost_abyss/frost_bridge.gd\""
const VERSION: String = "0.3.5"
const SECRET_FIELDS: Array = ["token", "password", "pass", "ticket", "authorization", "secret", "api_key", "apikey"]
const ACTIONS: Array = ["sima.enter", "sima.move", "sima.fight", "sima.open", "sima.reliquia", "sima.blason", "sima.guarda", "sima.rest", "sima.trade", "sima.omen", "sima.tear", "sima.pacto"]
var game: Node
var net: Node
var panel: Node
var elapsed: float = 0.0
var busy: bool = false
var seen: String = ""
var response: Dictionary = {}
var last_action: Dictionary = {}
var last_error: String = ""
var seq: int = 0
var sima: Dictionary = {}
var peeks: Dictionary = {}
var last_read: int = -10000
var active: bool = false
var journal: Array = []
var waiting_id: int = -1
var receipt: Dictionary = {}
var cache_run: String = ""
var last_battle: Dictionary = {}
var last_loot: Dictionary = {}

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(DIR))
	_ensure_cfg()
	if FileAccess.file_exists(DIR + "/response.json"):
		var prev: Variant = JSON.parse_string(FileAccess.get_file_as_string(DIR + "/response.json"))
		if prev is Dictionary:
			seen = str(prev.get("id", ""))
	get_tree().auto_accept_quit = true

func _process(delta: float) -> void:
	elapsed += delta
	if elapsed < 0.4 or busy:
		return
	elapsed = 0.0
	_tick()

func _tick() -> void:
	busy = true
	game = get_node_or_null("/root/Game")
	net = get_node_or_null("/root/Net")
	if game == null or net == null:
		busy = false
		_snapshot()
		return
	if net.has_signal("_response_arrived") and not net.is_connected("_response_arrived", _capture):
		net.connect("_response_arrived", _capture)
	var cmd_v: Variant = JSON.parse_string(FileAccess.get_file_as_string(DIR + "/command.json")) if FileAccess.file_exists(DIR + "/command.json") else null
	if cmd_v is Dictionary:
		var cmd: Dictionary = cmd_v
		var ident: String = str(cmd.get("id", ""))
		var key: String = FileAccess.get_file_as_string(DIR + "/session.key").strip_edges() if FileAccess.file_exists(DIR + "/session.key") else ""
		if ident != "" and ident != seen and key.length() >= 32 and str(cmd.get("key", "")) == key:
			seen = ident
			response = await _command(cmd)
			response["id"] = ident
			if ACTIONS.has(str(cmd.get("method", ""))):
				last_action = response.duplicate(true)
			_save("response.json", response)
			# Publish the post-command state before any further network wait.
			last_read = Time.get_ticks_msec()
			_snapshot()
	if not _state().is_empty() and Time.get_ticks_msec() - last_read > 5000 and not _fighting():
		last_read = Time.get_ticks_msec()
		var r: Dictionary = await net.call("request", "sima.get", {})
		if r.get("ok", false) and r.get("data") is Dictionary:
			sima = r["data"]
		else:
			last_error = str(r.get("error", "sima.get failed"))
	_ensure_cfg()
	busy = false
	_snapshot()

func _capture() -> void:
	if waiting_id < 0:
		return
	var all_v: Variant = net.get("_results")
	if all_v is Dictionary and all_v.has(waiting_id):
		receipt = all_v[waiting_id].duplicate(true)

func _state() -> Dictionary:
	var st: Variant = game.get("state") if game != null else null
	return st if st is Dictionary else {}

func _main() -> Node:
	return get_tree().current_scene

func _fighting() -> bool:
	var main: Node = _main()
	return main != null and "_sima_en_curso" in main and bool(main.get("_sima_en_curso"))

func _find_panel(n: Node) -> Node:
	if n.get_script() != null and str(n.get_script().resource_path).ends_with("/sima_panel.gd"):
		return n
	for c in n.get_children():
		var found: Node = _find_panel(c)
		if found != null:
			return found
	return null

func _get_panel() -> Node:
	if is_instance_valid(panel):
		return panel
	panel = _find_panel(get_tree().root)
	if panel == null:
		var main: Node = _main()
		if main != null and main.has_method("_abre_menu_ciudad"):
			main.call("_abre_menu_ciudad", "Sima")
			await get_tree().process_frame
			panel = _find_panel(get_tree().root)
	return panel

func _command(cmd: Dictionary) -> Dictionary:
	var method: String = str(cmd.get("method", ""))
	var args: Dictionary = cmd.get("args", {}) if cmd.get("args") is Dictionary else {}
	if absf(Time.get_unix_time_from_system() - float(cmd.get("issuedAt", 0))) > 20.0:
		return {"ok": false, "error": "操作已过期"}
	if method == "local.ping":
		return {"ok": true, "version": VERSION}
	if method == "local.stop":
		active = false
		return {"ok": true}
	if method == "local.open":
		var p_open: Node = await _get_panel()
		return {"ok": p_open != null}
	if method == "local.skipReplay":
		var main: Node = _main()
		if not _fighting() or main == null:
			return {"ok": true, "skipped": false}
		var battle: Variant = main.get("battle")
		var skip_button: Variant = main.get("_btn_saltar_sima")
		if battle is Node and battle.has_method("salta_replay_sima") and skip_button is Button and skip_button.is_visible_in_tree():
			battle.call("salta_replay_sima")
			return {"ok": true, "skipped": true}
		return {"ok": true, "skipped": false}
	if _state().is_empty():
		return {"ok": false, "error": "请先登录游戏"}
	if method == "local.refresh":
		var refreshed: Dictionary = await net.call("request", "sima.get", {})
		if refreshed.get("ok", false) and refreshed.get("data") is Dictionary:
			sima = refreshed["data"].duplicate(true)
			peeks.clear()
			if is_instance_valid(panel):
				panel.set("_data", sima.duplicate(true))
		return _clean(refreshed)
	if _fighting():
		return {"ok": false, "error": "战斗仍在播放"}
	if method == "hero.setSkills":
		var hr: Dictionary = await net.call("request", method, args)
		if hr.get("ok", false) and hr.get("data") is Dictionary and hr["data"].get("state") is Dictionary:
			game.call("set_state", hr["data"]["state"])
		return {"ok": hr.get("ok", false), "error": hr.get("error", ""), "confirmed": true}
	if method == "sima.peek":
		var r: Dictionary = await net.call("request", method, args)
		if r.get("ok", false):
			peeks[str(args.get("nodoId", ""))] = r.get("data", {})
		return _clean(r)
	if not ACTIONS.has(method):
		return {"ok": false, "error": "不支持的操作"}
	var issued: float = float(cmd.get("issuedAt", 0))
	if absf(Time.get_unix_time_from_system() - issued) > 20.0:
		return {"ok": false, "error": "操作已过期"}
	var run: Dictionary = sima["run"].duplicate(true) if sima.get("run") is Dictionary else {}
	if str(cmd.get("runId", "")) != str(run.get("id", "")) or str(cmd.get("nodeId", "")) != str(run.get("nodo", "")):
		return {"ok": false, "error": "深渊状态已变化，重新决策"}
	if net.has_method("is_open") and not bool(net.call("is_open")):
		return {"ok": false, "retryable": true, "error": "连接中断，请求尚未发出"}
	var p: Node = await _get_panel()
	if p == null:
		return {"ok": false, "error": "无法打开深渊面板"}
	if bool(p.get("_req_inflight")):
		return {"ok": false, "error": "游戏正在处理请求，本次操作尚未发出", "retryable": true}
	p.set("_data", sima.duplicate(true))
	var state_before: Dictionary = _state()
	var battle_labels: Dictionary = _battle_labels(run, state_before)
	var inventory_observed: bool = state_before.get("inventory") is Array
	var inventory_ids: Dictionary = {}
	for item_v in state_before.get("inventory", []):
		if item_v is Dictionary and str(item_v.get("id", "")) != "":
			inventory_ids[str(item_v["id"])] = true
	active = true
	receipt = {}
	waiting_id = int(net.get("_next_id"))
	var request_id: int = waiting_id
	var request_started: int = Time.get_ticks_msec()
	if method == "sima.omen":
		await p.call("_omen", str(args.get("opcion", "")))
	elif method == "sima.fight" or method == "sima.open":
		await p.call("_pelea", method, args)
	else:
		await p.call("_pide", method, args)
	waiting_id = -1
	var data_v: Variant = p.get("_data")
	if data_v is Dictionary:
		sima = data_v.duplicate(true)
	last_loot = {"commandId": str(cmd.get("id", "")), "method": method, "time": Time.get_unix_time_from_system(), "inventoryObserved": inventory_observed and _state().get("inventory") is Array, "inventoryAdded": []}
	if bool(last_loot["inventoryObserved"]):
		for item_v in _state().get("inventory", []):
			if item_v is Dictionary and str(item_v.get("id", "")) != "" and not inventory_ids.has(str(item_v["id"])):
				last_loot["inventoryAdded"].append(_item_for_log(item_v))
	if receipt.get("data") is Dictionary and receipt["data"].get("botin") is Dictionary:
		var loot_receipt: Dictionary = _clean(receipt["data"]["botin"])
		if loot_receipt.get("piezas") is Array:
			var pieces: Array = []
			for item_v in loot_receipt["piezas"]:
				if item_v is Dictionary:
					pieces.append(_item_for_log(item_v))
			loot_receipt["piezas"] = pieces
		last_loot["botin"] = loot_receipt
	if receipt.get("data") is Dictionary and receipt["data"].get("result") is Dictionary:
		last_battle = {"commandId": str(cmd.get("id", "")), "time": Time.get_unix_time_from_system(), "method": method, "result": receipt["data"]["result"], "vista": receipt["data"].get("vista", {}), "before": run, "labels": battle_labels}
		last_battle["lootEvidence"] = last_loot.duplicate(true)
		_save("battle.json", last_battle)
	var confirmed: bool = not receipt.is_empty()
	var success: bool = confirmed and bool(receipt.get("ok", false))
	if success and (method == "sima.pacto" or method == "sima.tear"):
		peeks.clear()
	var elapsed_ms: int = Time.get_ticks_msec() - request_started
	var not_sent: bool = not confirmed and int(net.get("_next_id")) == request_id
	var err: String = str(receipt.get("error", "")) if confirmed else "操作 %s 等待 %.1f 秒未收到服务端回执（请求 %d），正在核对结果" % [method, elapsed_ms / 1000.0, request_id]
	if not_sent:
		err = "操作 %s 尚未发出，可重试" % method
	var entry: Dictionary = {"time": Time.get_unix_time_from_system(), "method": method, "args": args, "ok": success, "error": err, "requestId": request_id, "elapsedMs": elapsed_ms, "retryable": not_sent, "code": "" if confirmed else "receipt_missing"}
	journal.append(entry)
	if journal.size() > 100:
		journal.pop_front()
	return {"ok": success, "confirmed": confirmed, "method": method, "error": err, "requestId": request_id, "elapsedMs": elapsed_ms, "retryable": not_sent, "code": "" if confirmed else "receipt_missing"}

func _battle_labels(run: Dictionary, state_before: Dictionary) -> Dictionary:
	# Freeze display names with this battle, using the game's own catalog and locale.
	var labels: Dictionary = {"skills": {}, "reactions": {}}
	var content_v: Variant = game.get("content") if game != null else {}
	var content: Dictionary = content_v if content_v is Dictionary else {}
	for hero_v in run.get("heroes", []):
		if not hero_v is Dictionary:
			continue
		var hero: Dictionary = hero_v
		var names: Dictionary = {}
		var sources: Array = []
		for entry in content.get("heroCatalog", []):
			if entry is Dictionary and str(entry.get("defId", entry.get("id", ""))) != "" and str(entry.get("defId", entry.get("id", ""))) == str(hero.get("defId", "")):
				sources.append(entry)
		sources.append(hero)
		for entry in state_before.get("heroes", []):
			if entry is Dictionary and str(entry.get("id", "")) != "" and str(entry.get("id", "")) == str(hero.get("id", "")):
				sources.append(entry)
		for entry in sources:
			for skill_v in entry.get("skills", []):
				if skill_v is Dictionary:
					var sid: String = str(skill_v.get("id", ""))
					var name_text: String = str(skill_v.get("name", ""))
					if sid != "" and name_text != "":
						names[sid] = tr(name_text)
		labels["skills"][str(hero.get("id", ""))] = names
	for reaction_v in content.get("reactions", []):
		if reaction_v is Dictionary:
			var rid: String = str(reaction_v.get("id", ""))
			var name_text: String = str(reaction_v.get("name", ""))
			if rid != "" and name_text != "":
				labels["reactions"][rid] = tr(name_text)
	return labels

func _item_for_log(item: Dictionary) -> Dictionary:
	var out: Dictionary = {}
	for key in ["id", "name", "nameEn", "nameEs", "nameZh", "nombre", "rarity", "nameParts"]:
		if item.has(key):
			out[key] = item[key]
	var naming: Script = load("res://util/nombre_objeto.gd") if ResourceLoader.exists("res://util/nombre_objeto.gd") else null
	if naming != null and naming.has_method("de"):
		out["displayName"] = str(naming.call("de", item))
	return out

func _snapshot() -> void:
	seq += 1
	var st: Dictionary = _state()
	var heroes: Array = []
	for h_v in st.get("heroes", []):
		if h_v is Dictionary:
			var h: Dictionary = h_v
			var item: Dictionary = {}
			for k in ["id", "defId", "name", "nombre", "level", "stats", "skills", "skillSlots", "tacticas", "prioLanz"]:
				if h.has(k):
					item[k] = h[k]
			heroes.append(item)
	var omen: Variant = panel.get("_omen_ev") if is_instance_valid(panel) else {}
	var content: Variant = game.get("content") if game != null else {}
	var defs: Dictionary = {}
	if content is Dictionary:
		for k in content:
			if str(k).to_lower().contains("sima"):
				defs[k] = content[k]
	var run_v: Variant = sima.get("run")
	var rid: String = str(run_v.get("id", "")) if run_v is Dictionary else ""
	if rid != cache_run:
		peeks.clear()
		cache_run = rid
	_save("snapshot.json", {"version": VERSION, "seq": seq, "pid": OS.get_process_id(), "time": Time.get_unix_time_from_system(), "loggedIn": not st.is_empty(), "fighting": _fighting(), "busy": busy, "active": active, "sima": sima, "peeks": peeks, "omen": omen, "heroes": heroes, "team": st.get("team", []), "defs": defs, "tacticasCatalogo": st.get("tacticasCatalogo", {}), "lastBattle": last_battle, "lastLoot": last_loot, "error": last_error, "lastCommand": response, "lastAction": last_action, "journal": journal})

func _clean(v: Variant) -> Variant:
	if v is Dictionary:
		var out: Dictionary = {}
		for k in v:
			out[k] = "<redacted>" if SECRET_FIELDS.has(str(k).to_lower()) else _clean(v[k])
		return out
	if v is Array:
		var out: Array = []
		for c in v:
			out.append(_clean(c))
		return out
	return v

func _save(name: String, value: Variant) -> void:
	var target: String = ProjectSettings.globalize_path(DIR + "/" + name)
	var f: FileAccess = FileAccess.open(target + ".tmp", FileAccess.WRITE)
	if f == null:
		return
	f.store_string(JSON.stringify(_clean(value)))
	f.close()
	DirAccess.rename_absolute(target + ".tmp", target)

func _ensure_cfg() -> void:
	var path: String = ProjectSettings.globalize_path(CFG)
	var text: String = FileAccess.get_file_as_string(path) if FileAccess.file_exists(path) else ""
	if text.contains(LINE):
		return
	if text.contains("[autoload]"):
		text = text.replace("[autoload]", "[autoload]\n\n" + LINE)
	else:
		text += "\n\n[autoload]\n\n" + LINE + "\n"
	var f: FileAccess = FileAccess.open(path + ".frost.tmp", FileAccess.WRITE)
	if f != null:
		f.store_string(text)
		f.close()
		DirAccess.rename_absolute(path + ".frost.tmp", path)
