import 'dart:convert';
import 'dart:io';

/// Finds the shared data directory that both ports read.
final Directory dataDirectory = _locate();

Directory _locate() {
  Directory dir = Directory.current;
  while (true) {
    final Directory candidate = Directory('${dir.path}/data');
    if (File('${candidate.path}/seed.json').existsSync()) {
      return candidate;
    }
    final Directory parent = dir.parent;
    if (parent.path == dir.path) {
      throw StateError('Cannot find the shared data directory.');
    }
    dir = parent;
  }
}

String readData(String name) =>
    File('${dataDirectory.path}/$name').readAsStringSync();

List<int> readDataBytes(String name) =>
    File('${dataDirectory.path}/$name').readAsBytesSync();

Map<String, dynamic> readJson(String name) =>
    jsonDecode(readData(name)) as Map<String, dynamic>;

Map<String, dynamic> readFixture(String name) =>
    jsonDecode(File('${dataDirectory.path}/testdata/$name').readAsStringSync())
        as Map<String, dynamic>;

/// The system word list, or null when this machine has none.
List<String>? englishWords() {
  final File file = File('/usr/share/dict/words');
  return file.existsSync() ? file.readAsLinesSync() : null;
}
