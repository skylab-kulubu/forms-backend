namespace Skylab.Forms.Application.Common;

public sealed class StorageConflictException(Exception inner) : Exception("Kayıt aynı anda başka bir işlemle değişti.", inner);
