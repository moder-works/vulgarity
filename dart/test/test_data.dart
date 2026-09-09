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
      throw StateError(
        'Cannot find the shared data directory. These tests read ../data/ at '
        'the repository root, which is outside this package, so they only run '
        'from a full clone of the repository. A published archive holds the '
        'package alone and cannot run them. Clone '
        'https://github.com/moder-works/vulgarity and run `dart test` from '
        'its dart/ directory.',
      );
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

/// The number of terms the English seed holds.
///
/// A filter built from seed.json must report exactly this many. Read it here
/// rather than writing the number into a test: the seed is generated, it grows,
/// and a literal only records what it happened to be on the day.
int seedEntryCount() =>
    (readJson('seed.json')['entries'] as List<dynamic>).length;

Map<String, dynamic> readFixture(String name) =>
    jsonDecode(File('${dataDirectory.path}/testdata/$name').readAsStringSync())
        as Map<String, dynamic>;

/// The system word list, or null when this machine has none.
List<String>? englishWords() {
  final File file = File('/usr/share/dict/words');
  return file.existsSync() ? file.readAsLinesSync() : null;
}
