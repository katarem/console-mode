import 'package:flutter/material.dart';

import 'screens/home_screen.dart';
import 'services/storage.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final storage = await AppStorage.load();
  runApp(ConsoleModeApp(storage: storage));
}

class ConsoleModeApp extends StatelessWidget {
  const ConsoleModeApp({super.key, required this.storage});

  final AppStorage storage;

  static const seed = Color(0xFF4C46E5);

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Console Mode',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(colorSchemeSeed: seed, useMaterial3: true),
      darkTheme: ThemeData(colorSchemeSeed: seed, brightness: Brightness.dark, useMaterial3: true),
      home: HomeScreen(storage: storage),
    );
  }
}
