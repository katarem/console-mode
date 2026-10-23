import 'package:flutter/material.dart';

import '../services/models.dart';

/// Interruptor grande PC / Consola con un "pulgar" animado.
class ModeToggle extends StatelessWidget {
  const ModeToggle({
    super.key,
    required this.mode,
    required this.enabled,
    required this.busy,
    required this.onChanged,
  });

  final PcMode? mode;
  final bool enabled;
  final bool busy;
  final ValueChanged<PcMode> onChanged;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final isConsole = mode == PcMode.console;

    return AspectRatio(
      aspectRatio: 0.62,
      child: Container(
        decoration: BoxDecoration(
          color: scheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(40),
        ),
        padding: const EdgeInsets.all(8),
        child: Stack(
          children: [
            if (mode != null)
              AnimatedAlign(
                duration: const Duration(milliseconds: 280),
                curve: Curves.easeOutCubic,
                alignment: isConsole ? Alignment.bottomCenter : Alignment.topCenter,
                child: FractionallySizedBox(
                  heightFactor: 0.5,
                  widthFactor: 1,
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 280),
                    decoration: BoxDecoration(
                      color: enabled ? scheme.primary : scheme.outline,
                      borderRadius: BorderRadius.circular(32),
                      boxShadow: [
                        BoxShadow(color: scheme.shadow.withValues(alpha: 0.25), blurRadius: 12, offset: const Offset(0, 4)),
                      ],
                    ),
                  ),
                ),
              ),
            Column(
              children: [
                _half(context, PcMode.pc, Icons.desktop_windows_outlined, 'Modo PC', 'Escritorio y audio del PC'),
                _half(context, PcMode.console, Icons.sports_esports, 'Modo Consola', 'TV + Steam Big Picture'),
              ],
            ),
            if (busy)
              Positioned.fill(
                child: Center(
                  child: Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(color: scheme.surface, shape: BoxShape.circle),
                    child: const SizedBox(width: 36, height: 36, child: CircularProgressIndicator()),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _half(BuildContext context, PcMode value, IconData icon, String title, String subtitle) {
    final theme = Theme.of(context);
    final selected = mode == value;
    final color = selected ? theme.colorScheme.onPrimary : theme.colorScheme.onSurfaceVariant;

    return Expanded(
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          borderRadius: BorderRadius.circular(32),
          onTap: enabled && !busy && !selected ? () => onChanged(value) : null,
          child: Center(
            child: AnimatedDefaultTextStyle(
              duration: const Duration(milliseconds: 200),
              style: theme.textTheme.titleLarge!.copyWith(color: color, fontWeight: FontWeight.w600),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(icon, size: 64, color: color),
                  const SizedBox(height: 12),
                  Text(title),
                  const SizedBox(height: 4),
                  Text(subtitle, style: theme.textTheme.bodyMedium!.copyWith(color: color)),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
