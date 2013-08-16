using System;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.Encoders;

namespace Org.BouncyCastle.Crypto.Generators
{
	// TODO Update docs to mention FIPS 186-3 when done
    /**
     * Generate suitable parameters for DSA, in line with FIPS 186-2.
     */
    public class DsaParametersGenerator
    {
		private int				L, N;
        private int				certainty;
        private SecureRandom	random;

        /**
         * initialise the key generator.
         *
         * @param size size of the key (range 2^512 -> 2^1024 - 64 bit increments)
         * @param certainty measure of robustness of prime (for FIPS 186-2 compliance this should be at least 80).
         * @param random random byte source.
         */
        public void Init(
            int             size,
            int             certainty,
            SecureRandom    random)
        {
			if (!IsValidDsaStrength(size))
				throw new ArgumentException(@"size must be from 512 - 1024 and a multiple of 64", "size");

			Init(size, GetDefaultN(size), certainty, random);
		}

		// TODO Make public to enable support for DSA keys > 1024 bits
		private void Init(
			int				L,
			int				N,
			int				certainty,
			SecureRandom	random)
		{
			// TODO Check that the (L, N) pair is in the list of acceptable (L, N pairs) (see Section 4.2)
			// TODO Should we enforce the minimum 'certainty' values as per C.3 Table C.1?

			this.L = L;
			this.N = N;
			this.certainty = certainty;
			this.random = random;
		}

//        /**
//         * add value to b, returning the result in a. The a value is treated
//         * as a IBigInteger of length (a.Length * 8) bits. The result is
//         * modulo 2^a.Length in case of overflow.
//         */
//        private static void Add(
//            byte[]  a,
//            byte[]  b,
//            int     value)
//        {
//            int     x = (b[b.Length - 1] & 0xff) + value;
//
//            a[b.Length - 1] = (byte)x;
//            x = (int) ((uint) x >>8);
//
//            for (int i = b.Length - 2; i >= 0; i--)
//            {
//                x += (b[i] & 0xff);
//                a[i] = (byte)x;
//                x = (int) ((uint) x >>8);
//            }
//        }

		/**
		 * which Generates the p and g values from the given parameters,
		 * returning the DsaParameters object.
		 * <p>
		 * Note: can take a while...</p>
		 */
		public DsaParameters GenerateParameters()
		{
			return L > 1024
				?	GenerateParameters_FIPS186_3()
				:	GenerateParameters_FIPS186_2();
		}

		private DsaParameters GenerateParameters_FIPS186_2()
		{
            byte[] seed = new byte[20];
            byte[] part1 = new byte[20];
            byte[] part2 = new byte[20];
            byte[] u = new byte[20];
            Sha1Digest sha1 = new Sha1Digest();
			int n = (L - 1) / 160;
			byte[] w = new byte[L / 8];

			for (;;)
			{
				random.NextBytes(seed);

				Hash(sha1, seed, part1);
				Array.Copy(seed, 0, part2, 0, seed.Length);
				Inc(part2);
				Hash(sha1, part2, part2);

				for (int i = 0; i != u.Length; i++)
				{
					u[i] = (byte)(part1[i] ^ part2[i]);
				}

				u[0] |= (byte)0x80;
				u[19] |= (byte)0x01;

				IBigInteger q = new BigInteger(1, u);

				if (!q.IsProbablePrime(certainty))
					continue;

				byte[] offset = Arrays.Clone(seed);
				Inc(offset);

				for (int counter = 0; counter < 4096; ++counter)
				{
					for (int k = 0; k < n; k++)
					{
						Inc(offset);
						Hash(sha1, offset, part1);
						Array.Copy(part1, 0, w, w.Length - (k + 1) * part1.Length, part1.Length);
					}

					Inc(offset);
					Hash(sha1, offset, part1);
					Array.Copy(part1, part1.Length - ((w.Length - (n) * part1.Length)), w, 0, w.Length - n * part1.Length);

					w[0] |= (byte)0x80;

					IBigInteger x = new BigInteger(1, w);

					IBigInteger c = x.Mod(q.ShiftLeft(1));

					IBigInteger p = x.Subtract(c.Subtract(BigInteger.One));

					if (p.BitLength != L)
						continue;

					if (p.IsProbablePrime(certainty))
					{
						IBigInteger g = CalculateGenerator_FIPS186_2(p, q, random);

						return new DsaParameters(p, q, g, new DsaValidationParameters(seed, counter));
					}
				}
			}
		}

		private static IBigInteger CalculateGenerator_FIPS186_2(IBigInteger p, IBigInteger q, SecureRandom r)
		{
			IBigInteger e = p.Subtract(BigInteger.One).Divide(q);
			IBigInteger pSub2 = p.Subtract(BigInteger.Two);

			for (;;)
			{
				IBigInteger h = BigIntegers.CreateRandomInRange(BigInteger.Two, pSub2, r);
				IBigInteger g = h.ModPow(e, p);

				if (g.BitLength > 1)
					return g;
			}
		}

		/**
		 * generate suitable parameters for DSA, in line with
		 * <i>FIPS 186-3 A.1 Generation of the FFC Primes p and q</i>.
		 */
		private DsaParameters GenerateParameters_FIPS186_3()
		{
// A.1.1.2 Generation of the Probable Primes p and q Using an Approved Hash Function
			// FIXME This should be configurable (digest size in bits must be >= N)
			IDigest d = new Sha256Digest();
			int outlen = d.GetDigestSize() * 8;

// 1. Check that the (L, N) pair is in the list of acceptable (L, N pairs) (see Section 4.2). If
//    the pair is not in the list, then return INVALID.
			// Note: checked at initialisation
			
// 2. If (seedlen < N), then return INVALID.
			// FIXME This should be configurable (must be >= N)
			int seedlen = N;
			byte[] seed = new byte[seedlen / 8];

// 3. n = ceiling(L ⁄ outlen) – 1.
			int n = (L - 1) / outlen;

// 4. b = L – 1 – (n ∗ outlen).
			int b = (L - 1) % outlen;

			byte[] output = new byte[d.GetDigestSize()];
			for (;;)
			{
// 5. Get an arbitrary sequence of seedlen bits as the domain_parameter_seed.
				random.NextBytes(seed);

// 6. U = Hash (domain_parameter_seed) mod 2^(N–1).
				Hash(d, seed, output);
				IBigInteger U = new BigInteger(1, output).Mod(BigInteger.One.ShiftLeft(N - 1));

// 7. q = 2^(N–1) + U + 1 – ( U mod 2).
				IBigInteger q = BigInteger.One.ShiftLeft(N - 1).Add(U).Add(BigInteger.One).Subtract(
					U.Mod(BigInteger.Two));

// 8. Test whether or not q is prime as specified in Appendix C.3.
				// TODO Review C.3 for primality checking
				if (!q.IsProbablePrime(certainty))
				{
// 9. If q is not a prime, then go to step 5.
					continue;
				}

// 10. offset = 1.
				// Note: 'offset' value managed incrementally
				byte[] offset = Arrays.Clone(seed);

// 11. For counter = 0 to (4L – 1) do
				int counterLimit = 4 * L;
				for (int counter = 0; counter < counterLimit; ++counter)
				{
// 11.1 For j = 0 to n do
//      Vj = Hash ((domain_parameter_seed + offset + j) mod 2^seedlen).
// 11.2 W = V0 + (V1 ∗ 2^outlen) + ... + (V^(n–1) ∗ 2^((n–1) ∗ outlen)) + ((Vn mod 2^b) ∗ 2^(n ∗ outlen)).
					// TODO Assemble w as a byte array
					IBigInteger W = BigInteger.Zero;
					for (int j = 0, exp = 0; j <= n; ++j, exp += outlen)
					{
						Inc(offset);
						Hash(d, offset, output);

						IBigInteger Vj = new BigInteger(1, output);
						if (j == n)
						{
							Vj = Vj.Mod(BigInteger.One.ShiftLeft(b));
						}

						W = W.Add(Vj.ShiftLeft(exp));
					}

// 11.3 X = W + 2^(L–1). Comment: 0 ≤ W < 2L–1; hence, 2L–1 ≤ X < 2L.
					IBigInteger X = W.Add(BigInteger.One.ShiftLeft(L - 1));

// 11.4 c = X mod 2q.
					IBigInteger c = X.Mod(q.ShiftLeft(1));

// 11.5 p = X - (c - 1). Comment: p ≡ 1 (mod 2q).
					IBigInteger p = X.Subtract(c.Subtract(BigInteger.One));

					// 11.6 If (p < 2^(L - 1)), then go to step 11.9
					if (p.BitLength != L)
						continue;

// 11.7 Test whether or not p is prime as specified in Appendix C.3.
					// TODO Review C.3 for primality checking
					if (p.IsProbablePrime(certainty))
					{
// 11.8 If p is determined to be prime, then return VALID and the values of p, q and
//      (optionally) the values of domain_parameter_seed and counter.
						// TODO Make configurable (8-bit unsigned)?
//	                    int index = 1;
//	                    IBigInteger g = CalculateGenerator_FIPS186_3_Verifiable(d, p, q, seed, index);
//	                    if (g != null)
//	                    {
//	                        // TODO Should 'index' be a part of the validation parameters?
//	                        return new DsaParameters(p, q, g, new DsaValidationParameters(seed, counter));
//	                    }

						IBigInteger g = CalculateGenerator_FIPS186_3_Unverifiable(p, q, random);
						return new DsaParameters(p, q, g, new DsaValidationParameters(seed, counter));
					}

// 11.9 offset = offset + n + 1.      Comment: Increment offset; then, as part of
//                                    the loop in step 11, increment counter; if
//                                    counter < 4L, repeat steps 11.1 through 11.8.
					// Note: 'offset' value already incremented in inner loop
				}
// 12. Go to step 5.
			}
		}

		private static IBigInteger CalculateGenerator_FIPS186_3_Unverifiable(IBigInteger p, IBigInteger q,
			SecureRandom r)
		{
			return CalculateGenerator_FIPS186_2(p, q, r);
		}

		private static IBigInteger CalculateGenerator_FIPS186_3_Verifiable(IDigest d, IBigInteger p, IBigInteger q,
			byte[] seed, int index)
		{
			// A.2.3 Verifiable Canonical Generation of the Generator g
			IBigInteger e = p.Subtract(BigInteger.One).Divide(q);
			byte[] ggen = Hex.Decode("6767656E");

			// 7. U = domain_parameter_seed || "ggen" || index || count.
			byte[] U = new byte[seed.Length + ggen.Length + 1 + 2];
			Array.Copy(seed, 0, U, 0, seed.Length);
			Array.Copy(ggen, 0, U, seed.Length, ggen.Length);
			U[U.Length - 3] = (byte)index; 

			byte[] w = new byte[d.GetDigestSize()];
			for (int count = 1; count < (1 << 16); ++count)
			{
				Inc(U);
				Hash(d, U, w);
				IBigInteger W = new BigInteger(1, w);
				IBigInteger g = W.ModPow(e, p);

				if (g.CompareTo(BigInteger.Two) >= 0)
					return g;
			}

			return null;
		}
		
		private static bool IsValidDsaStrength(
			int strength)
		{
			return strength >= 512 && strength <= 1024 && strength % 64 == 0;
		}

		private static void Hash(IDigest d, byte[] input, byte[] output)
		{
			d.BlockUpdate(input, 0, input.Length);
			d.DoFinal(output, 0);
		}

		private static int GetDefaultN(int L)
		{
			return L > 1024 ? 256 : 160;
		}

		private static void Inc(byte[] buf)
		{
			for (int i = buf.Length - 1; i >= 0; --i)
			{
				byte b = (byte)(buf[i] + 1);
				buf[i] = b;

				if (b != 0)
					break;
			}
		}
	}
}