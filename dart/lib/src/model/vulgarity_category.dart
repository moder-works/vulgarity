/// The kind of term that matched.
enum VulgarityCategory {
  /// A category the base list does not define.
  other,

  /// General swearing and insults.
  profanity,

  /// Sexual and anatomical terms.
  sexual,

  /// Slurs that target a group.
  hate,

  /// Threats and violent acts.
  violence,

  /// Illegal drugs and drug use.
  drug;

  /// Every category name a seed or preset can use, in [values] order.
  static final List<String> allNames = List<String>.unmodifiable(
      values.map((VulgarityCategory value) => value.name));

  /// Maps a category name, or returns null when the name is unknown.
  static VulgarityCategory? tryParse(String name) {
    for (final VulgarityCategory value in VulgarityCategory.values) {
      if (value.name == name) {
        return value;
      }
    }
    return null;
  }

  /// Maps a category name from a seed file.
  ///
  /// A name the base list does not define maps to [other].
  static VulgarityCategory parse(String name) {
    return switch (name) {
      'profanity' => VulgarityCategory.profanity,
      'sexual' => VulgarityCategory.sexual,
      'hate' => VulgarityCategory.hate,
      'violence' => VulgarityCategory.violence,
      'drug' => VulgarityCategory.drug,
      _ => VulgarityCategory.other,
    };
  }
}
