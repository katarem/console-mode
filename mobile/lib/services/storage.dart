import 'dart:convert';
import 'dart:io';
import 'dart:math';

import 'package:device_info_plus/device_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'models.dart';

/// Guarda la identidad de este móvil y los PCs emparejados.
class AppStorage {
  AppStorage._(this._prefs, this.deviceId, this._deviceName, this._pcs);

  static const _kDeviceId = 'device_id';
  static const _kDeviceName = 'device_name';
  static const _kPcs = 'pcs';
  static const _kWakeRequests = 'wake_requests';

  final SharedPreferences _prefs;
  final String deviceId;
  String _deviceName;
  final List<SavedPc> _pcs;

  String get deviceName => _deviceName;
  List<SavedPc> get pcs => List.unmodifiable(_pcs);

  static Future<AppStorage> load() async {
    final prefs = await SharedPreferences.getInstance();

    var deviceId = prefs.getString(_kDeviceId);
    if (deviceId == null) {
      deviceId = _randomId();
      await prefs.setString(_kDeviceId, deviceId);
    }

    var deviceName = prefs.getString(_kDeviceName);
    if (deviceName == null) {
      deviceName = await _defaultDeviceName();
      await prefs.setString(_kDeviceName, deviceName);
    }

    final pcs = <SavedPc>[];
    final raw = prefs.getString(_kPcs);
    if (raw != null) {
      try {
        for (final item in jsonDecode(raw) as List) {
          pcs.add(SavedPc.fromJson(item as Map<String, dynamic>));
        }
      } catch (_) {
        // Datos corruptos: empezamos de cero.
      }
    }

    return AppStorage._(prefs, deviceId, deviceName, pcs);
  }

  Future<void> setDeviceName(String name) async {
    _deviceName = name.trim().isEmpty ? _deviceName : name.trim();
    await _prefs.setString(_kDeviceName, _deviceName);
  }

  /// Añade o reemplaza (por id) un PC.
  Future<void> upsertPc(SavedPc pc) async {
    final index = _pcs.indexWhere((p) => p.id == pc.id);
    if (index >= 0) {
      _pcs[index] = pc;
    } else {
      _pcs.add(pc);
    }
    await _save();
  }

  Future<void> removePc(String id) async {
    _pcs.removeWhere((p) => p.id == id);
    await clearWakeRequest(id);
    await _save();
  }

  // ---------- Peticiones de encendido ----------
  // Se guardan para que, aunque la app pase a segundo plano o se cierre mientras Windows arranca, al conectar con el
  // PC se pida el modo elegido.

  WakeRequest? wakeRequest(String pcId) {
    try {
      final all = jsonDecode(_prefs.getString(_kWakeRequests) ?? '{}') as Map<String, dynamic>;
      final json = all[pcId];
      return json == null ? null : WakeRequest.fromJson(json as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  Future<void> setWakeRequest(String pcId, WakeRequest request) => _updateWakeRequests((all) => all[pcId] = request.toJson());

  Future<void> clearWakeRequest(String pcId) => _updateWakeRequests((all) => all.remove(pcId));

  Future<void> _updateWakeRequests(void Function(Map<String, dynamic> all) change) async {
    Map<String, dynamic> all;
    try {
      all = jsonDecode(_prefs.getString(_kWakeRequests) ?? '{}') as Map<String, dynamic>;
    } catch (_) {
      all = {};
    }
    change(all);
    await _prefs.setString(_kWakeRequests, jsonEncode(all));
  }

  Future<void> _save() =>
      _prefs.setString(_kPcs, jsonEncode(_pcs.map((p) => p.toJson()).toList()));

  static String _randomId() {
    final rnd = Random.secure();
    return List.generate(16, (_) => rnd.nextInt(256).toRadixString(16).padLeft(2, '0')).join();
  }

  static Future<String> _defaultDeviceName() async {
    try {
      final info = DeviceInfoPlugin();
      if (Platform.isAndroid) {
        final a = await info.androidInfo;
        final brand = a.manufacturer.isEmpty
            ? ''
            : a.manufacturer[0].toUpperCase() + a.manufacturer.substring(1);
        return '$brand ${a.model}'.trim();
      }
      if (Platform.isIOS) {
        final i = await info.iosInfo;
        return i.name.isNotEmpty ? i.name : i.model;
      }
    } catch (_) {
      // Sin información del dispositivo.
    }
    return 'Móvil';
  }
}
