import 'package:flutter/material.dart';

import '../services/models.dart';
import '../services/pairing.dart';
import '../services/storage.dart';

/// Pide al PC que confíe en este móvil y espera a que el usuario lo acepte allí.
class PairingScreen extends StatefulWidget {
  const PairingScreen({super.key, required this.target, required this.storage});

  final PairTarget target;
  final AppStorage storage;

  @override
  State<PairingScreen> createState() => _PairingScreenState();
}

class _PairingScreenState extends State<PairingScreen> {
  String _message = 'Conectando con el PC…';
  bool _waiting = true;
  bool _failed = false;

  @override
  void initState() {
    super.initState();
    _pair();
  }

  Future<void> _pair() async {
    setState(() {
      _message = 'Conectando con ${widget.target.name ?? 'el PC'}…';
      _waiting = true;
      _failed = false;
    });
    try {
      final pc = await Pairing.pair(
        widget.target,
        widget.storage,
        onWaitingForApproval: (pcName) {
          if (mounted) {
            setState(() => _message = 'Acepta la solicitud en la pantalla de «$pcName».\n\n'
                'Aparecerá como «${widget.storage.deviceName}».');
          }
        },
      );
      if (mounted) Navigator.of(context).pop(pc);
    } catch (e) {
      if (mounted) {
        setState(() {
          _message = '$e';
          _waiting = false;
          _failed = true;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Emparejar PC')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (_waiting)
                const SizedBox(width: 56, height: 56, child: CircularProgressIndicator())
              else
                Icon(Icons.error_outline, size: 56, color: theme.colorScheme.error),
              const SizedBox(height: 24),
              Text(_message, textAlign: TextAlign.center, style: theme.textTheme.titleMedium),
              if (_failed) ...[
                const SizedBox(height: 24),
                FilledButton(onPressed: _pair, child: const Text('Reintentar')),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
