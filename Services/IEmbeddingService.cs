namespace diplom.Services
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
    }

    public class SimpleEmbeddingService : IEmbeddingService
    {
        private readonly Random _random = new Random();


        public interface IEmbeddingService
        {
            Task<float[]> GenerateEmbeddingAsync(string text);
            float CosineSimilarity(float[] vec1, float[] vec2);
        }

        public Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var hash = text.GetHashCode();
            var rng = new Random(hash);
            var embedding = new float[384];

            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = (float)rng.NextDouble();
            }

            // Нормализация
            float norm = 0;
            for (int i = 0; i < embedding.Length; i++)
            {
                norm += embedding[i] * embedding[i];
            }
            norm = (float)Math.Sqrt(norm);

            if (norm > 0)
            {
                for (int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] /= norm;
                }
            }

            return Task.FromResult(embedding);
        }
    }
}