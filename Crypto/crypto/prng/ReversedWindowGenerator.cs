using System;

namespace Org.BouncyCastle.Crypto.Prng
{
    /// <remarks>
    /// Takes bytes generated by an underling RandomGenerator and reverses the order in
    /// each small window (of configurable size).
    /// <p>
    /// Access to internals is synchronized so a single one of these can be shared.
    /// </p>
    /// </remarks>
    public class ReversedWindowGenerator : IRandomGenerator
    {
        private readonly IRandomGenerator _generator;

        private readonly byte[] _window;
        private int _windowCount;

        public ReversedWindowGenerator(IRandomGenerator generator, int windowSize)
        {
            if (generator == null)
                throw new ArgumentNullException("generator");
            if (windowSize < 2)
                throw new ArgumentException("Window size must be at least 2", "windowSize");

            _generator = generator;
            _window = new byte[windowSize];
        }

        /// <summary>Add more seed material to the generator.</summary>
        /// <param name="seed">A byte array to be mixed into the generator's state.</param>
        public virtual void AddSeedMaterial(byte[] seed)
        {
            lock (this)
            {
                _windowCount = 0;
                _generator.AddSeedMaterial(seed);
            }
        }

        /// <summary>Add more seed material to the generator.</summary>
        /// <param name="seed">A long value to be mixed into the generator's state.</param>
        public virtual void AddSeedMaterial(long seed)
        {
            lock (this)
            {
                _windowCount = 0;
                _generator.AddSeedMaterial(seed);
            }
        }

        /// <summary>Fill byte array with random values.</summary>
        /// <param name="bytes">Array to be filled.</param>
        public virtual void NextBytes(byte[] bytes)
        {
            DoNextBytes(bytes, 0, bytes.Length);
        }

        /// <summary>Fill byte array with random values.</summary>
        /// <param name="bytes">Array to receive bytes.</param>
        /// <param name="start">Index to start filling at.</param>
        /// <param name="len">Length of segment to fill.</param>
        public virtual void NextBytes(byte[] bytes, int start, int len)
        {
            DoNextBytes(bytes, start, len);
        }

        private void DoNextBytes(byte[] bytes, int start, int len)
        {
            lock (this)
            {
                var done = 0;
                while (done < len)
                {
                    if (_windowCount < 1)
                    {
                        _generator.NextBytes(_window, 0, _window.Length);
                        _windowCount = _window.Length;
                    }

                    bytes[start + done++] = _window[--_windowCount];
                }
            }
        }
    }
}
