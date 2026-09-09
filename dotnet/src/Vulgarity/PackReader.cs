using System;
using System.Collections.Generic;
using System.Text;

namespace Vulgarity
{
    /// <summary>Reads a masked pack document into terms and allowlist words.</summary>
    /// <remarks>
    /// A pack carries the same data as a seed document, in a form that holds no
    /// readable text. The bundled term lists ship as packs, so an assembly no
    /// longer prints the whole list under a tool such as strings.
    ///
    /// This is obfuscation, not encryption. The key sits in this file, so anyone
    /// who wants the list can still get it. The goal is only that nobody reads
    /// it by accident. tool/packlib.py holds the format and the encoder.
    /// </remarks>
    internal static class PackReader
    {
        public const int SupportedSchema = 1;

        private const int FlagHasAllow = 0x01;

        private static readonly byte[] Magic = { (byte)'V', (byte)'P', (byte)'K', (byte)'1' };

        // The mask key. It must match KEY in tool/packlib.py.
        private static readonly byte[] Key = Encoding.ASCII.GetBytes("vulgarity-pack-v1");

        // Encoding.UTF8 replaces a bad byte with U+FFFD and carries on. Dart's
        // utf8.decode throws. This throws too, so both ports refuse the same
        // corrupt pack instead of one of them loading mangled terms.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>True when these bytes start with the pack magic.</summary>
        public static bool Looks(byte[] pack)
        {
            if (pack == null || pack.Length < Magic.Length)
            {
                return false;
            }

            for (int i = 0; i < Magic.Length; i++)
            {
                if (pack[i] != Magic[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static void Load(
            byte[] pack,
            string expectedProfile,
            List<VulgarityTerm> terms,
            List<string> allow)
        {
            if (pack == null)
            {
                throw new ArgumentNullException("pack");
            }

            if (!Looks(pack))
            {
                throw new FormatException("This is not a pack. The magic does not match.");
            }

            byte[] data = Unmask(pack, Magic.Length);
            if (data.Length < 2)
            {
                throw new FormatException("A pack must hold a header.");
            }

            int schema = data[0];
            if (schema != SupportedSchema)
            {
                throw new FormatException(
                    "This build reads pack schema " + SupportedSchema +
                    ". The file states schema " + schema + ".");
            }

            int flags = data[1];
            int pos = 2;

            // The profile pins the fold table the pack was built with. A stale file
            // then fails here instead of matching silently wrong.
            string profile = ReadText(data, ref pos);
            if (profile != expectedProfile)
            {
                throw new FormatException(
                    "This build implements fold profile '" + expectedProfile +
                    "'. The pack states '" + profile + "'.");
            }

            // The count is read from the pack, so it is not to be trusted with an
            // allocation. Grow the list while reading instead: a crafted count runs
            // the data out and fails as a malformed pack, not as an
            // OutOfMemoryException.
            int categoryCount = ReadVarint(data, ref pos);
            List<string> categories = new List<string>();
            for (int i = 0; i < categoryCount; i++)
            {
                categories.Add(ReadText(data, ref pos));
            }

            int entryCount = ReadVarint(data, ref pos);

            // An entry costs at least three bytes: a length, one character, and
            // the flags. Check that before building a list, so a hostile count
            // cannot make this allocate.
            if (entryCount > (data.Length - pos) / 3)
            {
                throw new FormatException("The pack claims more entries than it holds.");
            }

            for (int i = 0; i < entryCount; i++)
            {
                string term = ReadText(data, ref pos);
                if (term.Length == 0)
                {
                    throw new FormatException("A pack term must not be empty.");
                }

                if (pos >= data.Length)
                {
                    throw new FormatException("The pack ends inside an entry.");
                }

                int packed = data[pos];
                pos++;

                int index = packed >> 4;
                if (index >= categories.Count)
                {
                    throw new FormatException("A pack entry names a category the table does not hold.");
                }

                int severity = packed & 0x07;
                if (severity < 1 || severity > 5)
                {
                    throw new FormatException(
                        "Severity must be 1 to 5. Term '" + term + "' states " + severity + ".");
                }

                terms.Add(new VulgarityTerm(term, categories[index], severity, (packed & 0x08) != 0));
            }

            if ((flags & FlagHasAllow) != 0)
            {
                int allowCount = ReadVarint(data, ref pos);
                for (int i = 0; i < allowCount; i++)
                {
                    string word = ReadText(data, ref pos);
                    if (word.Length != 0)
                    {
                        allow.Add(word);
                    }
                }
            }

            // Nothing may follow. This catches a truncated pack and a padded one
            // in the same line.
            if (pos != data.Length)
            {
                throw new FormatException("The pack holds bytes after its last entry.");
            }
        }

        /// <summary>Strips the magic and XORs the rest with the keystream.</summary>
        private static byte[] Unmask(byte[] pack, int offset)
        {
            int length = pack.Length - offset;
            byte[] box = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                box[i] = (byte)i;
            }

            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + box[i] + Key[i % Key.Length]) & 0xFF;
                byte swap = box[i];
                box[i] = box[j];
                box[j] = swap;
            }

            byte[] out_ = new byte[length];
            int a = 0;
            int b = 0;
            for (int i = 0; i < length; i++)
            {
                a = (a + 1) & 0xFF;
                b = (b + box[a]) & 0xFF;
                byte swap = box[a];
                box[a] = box[b];
                box[b] = swap;
                out_[i] = (byte)(pack[offset + i] ^ box[(box[a] + box[b]) & 0xFF]);
            }

            return out_;
        }

        /// <summary>Reads an unsigned LEB128 number, capped at 31 bits.</summary>
        /// <remarks>
        /// <para>
        /// The cap is what keeps every runtime reading the same pack. Dart compiles
        /// its shift to JavaScript's on the web, which truncates to 32 bits, so a
        /// value of 2^32+5 would read as 5 there and as 2^32+5 on the VM. Rather
        /// than let a crafted pack load on one and fail on the other, a number wider
        /// than 31 bits is refused everywhere. It also keeps the result inside an
        /// int here, where a wider one used to overflow.
        /// </para>
        /// <para>
        /// In practice that means a fifth byte carries three bits at most, and there
        /// is never a sixth. tool/packlib.py refuses to write what this refuses to
        /// read.
        /// </para>
        /// </remarks>
        private static int ReadVarint(byte[] data, ref int pos)
        {
            int value = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= data.Length)
                {
                    throw new FormatException("The pack ends inside a number.");
                }

                int b = data[pos];
                pos++;

                // At the fifth byte only the low three bits are left, and a
                // continuation bit would ask for a sixth. Either is over the cap.
                if (shift == 28 && b > 0x07)
                {
                    throw new FormatException("A pack number runs too long.");
                }

                value |= (b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return value;
                }

                shift += 7;
                if (shift > 28)
                {
                    throw new FormatException("A pack number runs too long.");
                }
            }
        }

        private static string ReadText(byte[] data, ref int pos)
        {
            // The length is capped at 31 bits, so it is never negative. The
            // subtraction, rather than pos + length, keeps the check itself from
            // overflowing on a length near int.MaxValue.
            int length = ReadVarint(data, ref pos);
            if (length < 0 || length > data.Length - pos)
            {
                throw new FormatException("The pack ends inside a string.");
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(data, pos, length);
            }
            catch (DecoderFallbackException)
            {
                throw new FormatException("The pack holds text that is not valid UTF-8.");
            }

            pos += length;
            return text;
        }
    }
}
