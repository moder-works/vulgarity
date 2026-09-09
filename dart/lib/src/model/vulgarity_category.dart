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

/// How [VulgarityFilter.score] combines severities.
enum ScoreMode {
  /// Add up the severity of every match.
  total,

  /// Take the highest severity of any match.
  max,
}
