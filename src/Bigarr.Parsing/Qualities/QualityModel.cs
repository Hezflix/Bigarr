// Vendored from Radarr v6.3.0.10514 (7827e5368947f158ad06f757334f5cde6c406411)
// Original path: src/NzbDrone.Core/Qualities/QualityModel.cs — GPL-3.0
// Radarr is Copyright (C) Radarr contributors, licensed under the GNU General Public License v3.0.
// Bigarr modifications: see src/Bigarr.Parsing/VENDORED.md

using System;
using System.Text.Json.Serialization;

namespace Bigarr.Parsing.Qualities
{
    public class QualityModel : IEquatable<QualityModel>
    {
        public Quality Quality { get; set; }

        public Revision Revision { get; set; }

        [JsonIgnore]
        public QualityDetectionSource SourceDetectionSource { get; set; }

        [JsonIgnore]
        public QualityDetectionSource ResolutionDetectionSource { get; set; }

        [JsonIgnore]
        public QualityDetectionSource ModifierDetectionSource { get; set; }

        [JsonIgnore]
        public QualityDetectionSource RevisionDetectionSource { get; set; }

        public QualityModel()
            : this(Quality.Unknown, new Revision())
        {
        }

        public QualityModel(Quality quality, Revision revision = null)
        {
            Quality = quality;
            Revision = revision ?? new Revision();
        }

        public override string ToString()
        {
            return string.Format("{0} {1}", Quality, Revision);
        }

        public override int GetHashCode()
        {
            // Overflow is fine, just wrap
            unchecked
            {
                var hash = 17;
                hash = (hash * 23) + Revision.GetHashCode();
                hash = (hash * 23) + Quality.GetHashCode();
                return hash;
            }
        }

        public bool Equals(QualityModel other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other.Quality.Id.Equals(Quality.Id) && other.Revision.Equals(Revision);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return Equals(obj as QualityModel);
        }

        public static bool operator ==(QualityModel left, QualityModel right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(QualityModel left, QualityModel right)
        {
            return !Equals(left, right);
        }
    }
}
