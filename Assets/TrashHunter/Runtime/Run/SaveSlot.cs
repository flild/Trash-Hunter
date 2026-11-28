using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TrashHunter
{
    internal sealed class SaveSlot : IDisposable
    {
        [Serializable]
        private sealed class Envelope
        {
            public string payload;
            public string checksum;
        }

        [Serializable]
        private sealed class LegacyLocation
        {
            public bool atHome = false;
        }

        private sealed class UnsupportedFormat : Exception
        {
            public UnsupportedFormat() : base("Для этого автосохранения нужна другая версия игры.") { }
        }

        private readonly string primary;
        private readonly string backup;
        private readonly string temporary;
        private readonly Func<RunSnapshot, bool> validate;
        private readonly FileStream lease;
        private string savedChecksum;
        private bool usingBackup;
        private bool uncertain;

        public string RecoveryNotice { get; private set; }

        public SaveSlot(string folder, Func<RunSnapshot, bool> validate)
        {
            this.validate = validate;
            Directory.CreateDirectory(folder);
            primary = Path.Combine(folder, "slot.json");
            backup = Path.Combine(folder, "slot.backup.json");
            temporary = Path.Combine(folder, "slot.pending");
            lease = new FileStream(Path.Combine(folder, "slot.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }

        public RunSnapshot Load()
        {
            var snapshot = Read(primary, out savedChecksum, out var primaryDamaged);
            if (snapshot != null)
                return snapshot;

            snapshot = Read(backup, out savedChecksum, out var backupDamaged);
            if (snapshot == null)
            {
                if (primaryDamaged || backupDamaged)
                    throw new InvalidDataException("Основной файл и резервная копия повреждены или отсутствуют.");
                return null;
            }

            usingBackup = true;
            var time = new DateTime(snapshot.utcTicks, DateTimeKind.Utc).ToLocalTime();
            RecoveryNotice = $"Восстановлена более ранняя запись от {time:dd.MM.yyyy HH:mm:ss}.";
            return snapshot;
        }

        public bool TryWrite(RunSnapshot snapshot, out string error)
        {
            if (uncertain)
            {
                error = "Не удалось определить результат записи. Перезапустите игру, чтобы проверить автослот.";
                return false;
            }

            var envelope = new Envelope { payload = JsonUtility.ToJson(snapshot) };
            envelope.checksum = Checksum(envelope.payload);
            var publishing = false;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope));
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (Read(temporary, out var staged, out _) == null || staged != envelope.checksum)
                    throw new InvalidDataException("Проверка временного файла завершилась ошибкой.");

                var previous = Read(primary, out var presentChecksum, out var damaged);
                if (!PreviousIsAvailable(previous, presentChecksum, damaged))
                {
                    uncertain = true;
                    throw new InvalidDataException("Файл автослота изменился. Проверьте его после перезапуска.");
                }

                if (damaged)
                {
                    var archive = Path.Combine(Path.GetDirectoryName(primary), $"slot.damaged-{Guid.NewGuid():N}.json");
                    File.Copy(primary, archive);
                }

                publishing = true;
                if (previous != null || damaged)
                    File.Replace(temporary, primary, usingBackup ? null : backup);
                else
                    File.Move(temporary, primary);
            }
            catch (Exception exception) when (IsReadError(exception))
            {
                error = $"Не удалось сохранить игру. {Describe(exception)}";
                if (!publishing || !WasPublished(envelope.checksum))
                    return false;
            }

            savedChecksum = envelope.checksum;
            usingBackup = false;
            error = null;
            return true;
        }

        public void Dispose() => lease.Dispose();

        public static bool IsReadError(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException || exception is UnsupportedFormat ||
            exception is InvalidDataException || exception is ArgumentException || exception is NotSupportedException;

        public static string Describe(Exception exception)
        {
            if (exception is UnauthorizedAccessException)
                return "Нет доступа к файлам автослота.";
            if (exception is IOException)
                return "Не удалось обратиться к файлам автослота. Проверьте, что диск доступен и игра открыта один раз.";
            return exception.Message;
        }

        private bool PreviousIsAvailable(RunSnapshot previous, string checksum, bool damaged)
        {
            if (!usingBackup)
                return !damaged && checksum == savedChecksum && (previous != null || savedChecksum == null);

            return previous == null && Read(backup, out var copy, out _) != null && copy == savedChecksum;
        }

        private bool WasPublished(string expected)
        {
            try
            {
                var published = Read(primary, out var checksum, out var damaged);
                if (published != null && checksum == expected)
                    return true;

                uncertain = !PreviousIsAvailable(published, checksum, damaged);
            }
            catch (Exception exception) when (IsReadError(exception))
            {
                uncertain = true;
            }
            return false;
        }

        private RunSnapshot Read(string path, out string checksum, out bool damaged)
        {
            checksum = null;
            damaged = false;
            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                return null;
            }

            try
            {
                var envelope = JsonUtility.FromJson<Envelope>(json);
                if (!string.IsNullOrEmpty(envelope?.payload) && Checksum(envelope.payload) == envelope.checksum)
                {
                    var snapshot = JsonUtility.FromJson<RunSnapshot>(envelope.payload);
                    if (snapshot != null && snapshot.format > RunSnapshot.CurrentFormat)
                        throw new UnsupportedFormat();
                    if (snapshot?.format == 1)
                    {
                        snapshot.location = JsonUtility.FromJson<LegacyLocation>(envelope.payload).atHome
                            ? RunLocation.Home : RunLocation.Search;
                    }
                    if (snapshot?.format is 1 or 2)
                    {
                        snapshot.hasSold = snapshot.cash > 0;
                        snapshot.quests = Array.Empty<QuestProgress>();
                        snapshot.contacts = Array.Empty<string>();
                    }
                    if (snapshot?.format is >= 1 and <= 3)
                        snapshot.format = RunSnapshot.CurrentFormat;
                    if (snapshot != null && validate(snapshot))
                    {
                        checksum = envelope.checksum;
                        return snapshot;
                    }
                }
            }
            catch (ArgumentException)
            {
                // JsonUtility rejects malformed JSON before the remaining slot can be checked.
            }

            damaged = true;
            return null;
        }

        private static string Checksum(string payload)
        {
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
