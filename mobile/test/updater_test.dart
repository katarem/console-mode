import 'package:console_mode/services/updater.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, dynamic> release(String tag, List<String> assets, {bool prerelease = false}) => {
      'tag_name': tag,
      'html_url': 'https://github.com/katarem/console-mode/releases/tag/$tag',
      'draft': false,
      'prerelease': prerelease,
      'assets': [
        for (final a in assets)
          {'name': a, 'browser_download_url': 'https://github.com/katarem/console-mode/releases/download/$tag/$a'},
      ],
    };

void main() {
  test('compara versiones', () {
    expect(Updater.compareVersions('1.10.0', '1.9.9'), greaterThan(0));
    expect(Updater.compareVersions('1.3.3', '1.3.3'), 0);
    expect(Updater.compareVersions('1.3.3', '1.4.0'), lessThan(0));
  });

  test('elige el APK más nuevo aunque la última release no traiga APK', () {
    final releases = [
      release('v1.5.0', ['ConsoleMode-Setup-1.5.0.exe', 'ConsoleMode-1.5.0-portable.exe']),
      release('v1.4.0', ['ConsoleMode-Setup-1.4.0.exe', 'ConsoleMode-1.4.0.apk']),
      release('v1.3.3', ['ConsoleMode-1.3.3.apk']),
    ];
    final update = Updater.pickUpdate(releases, '1.3.3');
    expect(update?.version, '1.4.0');
    expect(update?.apkUrl, endsWith('/v1.4.0/ConsoleMode-1.4.0.apk'));
  });

  test('sin actualización si ya está al día', () {
    expect(Updater.pickUpdate([release('v1.4.0', ['ConsoleMode-1.4.0.apk'])], '1.4.0'), isNull);
  });

  test('ignora prereleases y enlaces de otros repositorios', () {
    final releases = [
      release('v2.0.0', ['ConsoleMode-2.0.0.apk'], prerelease: true),
      {
        'draft': false,
        'prerelease': false,
        'assets': [
          {'name': 'ConsoleMode-3.0.0.apk', 'browser_download_url': 'https://evil.example.com/ConsoleMode-3.0.0.apk'},
        ],
      },
    ];
    expect(Updater.pickUpdate(releases, '1.0.0'), isNull);
  });
}
