import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

/// Versión de esta app. La CI la incrusta al compilar (`--dart-define=APP_VERSION=x.y.z`);
/// en compilaciones locales queda vacía y no se buscan actualizaciones.
const appVersion = String.fromEnvironment('APP_VERSION');

class AppUpdate {
  AppUpdate({required this.version, required this.apkUrl, required this.releaseUrl});

  final String version;
  final String apkUrl;
  final String releaseUrl;
}

/// Busca versiones nuevas del APK en las releases de GitHub. Cada release puede no traer el APK
/// (si la app móvil no cambió), así que se mira en las últimas releases el APK más nuevo.
class Updater {
  static const repository = 'katarem/console-mode';
  static final _apkName = RegExp(r'^ConsoleMode-(\d+\.\d+\.\d+)\.apk$');
  static const _channel = MethodChannel('console_mode/system');

  /// Solo en Android y en compilaciones con versión (las de la CI).
  static bool get supported => Platform.isAndroid && appVersion.isNotEmpty;

  static Future<AppUpdate?> check() async {
    if (!supported) return null;

    final client = HttpClient()..connectionTimeout = const Duration(seconds: 10);
    try {
      final request = await client.getUrl(Uri.parse('https://api.github.com/repos/$repository/releases?per_page=20'));
      request.headers
        ..set(HttpHeaders.userAgentHeader, 'ConsoleMode-Android/$appVersion')
        ..set(HttpHeaders.acceptHeader, 'application/vnd.github+json');
      final response = await request.close().timeout(const Duration(seconds: 15));
      if (response.statusCode != 200) {
        throw HttpException('GitHub respondió ${response.statusCode}');
      }
      final releases = jsonDecode(await response.transform(utf8.decoder).join()) as List;
      return pickUpdate(releases, appVersion);
    } finally {
      client.close();
    }
  }

  /// Elige el APK más nuevo que sea posterior a [current]. Separado de [check] para poder probarlo.
  static AppUpdate? pickUpdate(List<dynamic> releases, String current) {
    AppUpdate? best;
    for (final release in releases.cast<Map<String, dynamic>>()) {
      if (release['draft'] == true || release['prerelease'] == true) continue;
      for (final asset in (release['assets'] as List? ?? const []).cast<Map<String, dynamic>>()) {
        final match = _apkName.firstMatch(asset['name'] as String? ?? '');
        final url = asset['browser_download_url'] as String? ?? '';
        if (match == null || !url.startsWith('https://github.com/$repository/releases/download/')) continue;
        final version = match.group(1)!;
        if (best == null || compareVersions(version, best.version) > 0) {
          best = AppUpdate(
            version: version,
            apkUrl: url,
            releaseUrl: release['html_url'] as String? ?? 'https://github.com/$repository/releases',
          );
        }
      }
    }
    return best != null && compareVersions(best.version, current) > 0 ? best : null;
  }

  /// Compara versiones «x.y.z»: negativo si a < b, 0 si son iguales, positivo si a > b.
  static int compareVersions(String a, String b) {
    List<int> parse(String v) => v.split('.').map((p) => int.tryParse(p) ?? 0).toList();
    final pa = parse(a), pb = parse(b);
    for (var i = 0; i < 3; i++) {
      final x = i < pa.length ? pa[i] : 0;
      final y = i < pb.length ? pb[i] : 0;
      if (x != y) return x - y;
    }
    return 0;
  }

  /// Abre el APK con Android: se descarga y, al tocar la notificación, se instala encima de esta versión.
  static Future<void> openApk(AppUpdate update) =>
      _channel.invokeMethod<void>('openUrl', {'url': update.apkUrl});
}
