using System.Collections.Generic;
using ModularMech.Data;

namespace ModularMech.Tests
{
    /// <summary>
    /// <see cref="IPartCatalog"/> のテスト用 POCO 実装。PartId をキーにした単純な辞書。
    /// </summary>
    public sealed class FakePartCatalog : IPartCatalog
    {
        private readonly Dictionary<string, IPartData> _parts = new Dictionary<string, IPartData>();

        /// <summary>チェーンして複数登録できるように自身を返す。</summary>
        public FakePartCatalog Add(IPartData part)
        {
            _parts[part.PartId] = part;
            return this;
        }

        public bool TryGet(string partId, out IPartData part)
        {
            return _parts.TryGetValue(partId, out part);
        }
    }
}
