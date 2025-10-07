namespace QABotPOC
{
    public class VectorStore
    {
        public class DocumentChunk
        {
            public string Text { get; set; }
            public float[] Embedding { get; set; }
        }

        private List<DocumentChunk> _chunks = new();

        public void AddChunk(string text, float[] embedding)
        {
            _chunks.Add(new DocumentChunk { Text = text, Embedding = embedding });
        }

        public DocumentChunk FindBestMatch(float[] queryEmbedding)
        {
            return _chunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = EmbeddingHelper.CosineSimilarity(queryEmbedding, chunk.Embedding)
                })
                .OrderByDescending(x => x.Score)
                .First().Chunk;
        }
    }
}
