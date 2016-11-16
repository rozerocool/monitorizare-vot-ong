﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using MonitorizareVot.Ong.Api.Common;
using MonitorizareVot.Ong.Api.ViewModels;
using Microsoft.IdentityModel.Tokens;
using Jwt;
using System.Collections.Generic;

namespace MonitorizareVot.Ong.Api.Controllers
{
    [Route("api/v1/auth")]
    public class JwtController : Controller
    {
        private static string SecretKey = "needtogetthisfromenvironment";
        private static SymmetricSecurityKey _signingKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SecretKey));
        private readonly JwtIssuerOptions _jwtOptions;

        private readonly ILogger _logger;
        private readonly JsonSerializerSettings _serializerSettings;

        public JwtController(JwtIssuerOptions jwtOptions, ILoggerFactory loggerFactory)
        {
            _jwtOptions = jwtOptions;
            ThrowIfInvalidOptions(_jwtOptions);

            _logger = loggerFactory.CreateLogger<JwtController>();

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };
        }


        [HttpPut]
        [AllowAnonymous]
        // this method will only be called the token is expired
        public async Task<IActionResult> RefreshLogin()
        {
            string token = Request.Headers["Authorization"];
            if (string.IsNullOrEmpty(token))
            {
                return Forbid();
            }
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring("Bearer ".Length).Trim();
            }
            if (string.IsNullOrEmpty(token))
            {
                return Forbid();
            }

            var decoded = JsonWebToken.DecodeToObject<Dictionary<string,string>>(token, SecretKey, false);
            var idOng = Int32.Parse(decoded["IdOng"]);
            var userName = decoded[JwtRegisteredClaimNames.Sub];

            var json = await generateToken(userName, idOng);
              
            return new OkObjectResult(json);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] ApplicationUser applicationUser)
        {
            var identity = await GetClaimsIdentity(applicationUser);
            if (identity == null)
            {
                _logger.LogInformation($"Invalid username ({applicationUser.UserName}) or password ({applicationUser.Password})");
                return BadRequest("Invalid credentials");
            }
            var json = await generateToken(applicationUser.UserName);
            
            return new OkObjectResult(json);
        }

        private async Task<string> generateToken(string userName, int idOng = 0) {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.Jti, await _jwtOptions.JtiGenerator()),
                new Claim(JwtRegisteredClaimNames.Iat,
                          ToUnixEpochDate(_jwtOptions.IssuedAt).ToString(),
                          ClaimValueTypes.Integer64),
                new Claim("IdOng",idOng.ToString())
            };

            // Create the JWT security token and encode it.
            var jwt = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: _jwtOptions.NotBefore,
                expires: _jwtOptions.Expiration,
                signingCredentials: _jwtOptions.SigningCredentials);

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            // Serialize and return the response
            var response = new
            {
                token = encodedJwt,
                expires_in = (int)_jwtOptions.ValidFor.TotalSeconds
            };

            var json = JsonConvert.SerializeObject(response, _serializerSettings);
            return json;
        }


        private static void ThrowIfInvalidOptions(JwtIssuerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            if (options.ValidFor <= TimeSpan.Zero)
            {
                throw new ArgumentException("Must be a non-zero TimeSpan.", nameof(JwtIssuerOptions.ValidFor));
            }

            if (options.SigningCredentials == null)
            {
                throw new ArgumentNullException(nameof(JwtIssuerOptions.SigningCredentials));
            }

            if (options.JtiGenerator == null)
            {
                throw new ArgumentNullException(nameof(JwtIssuerOptions.JtiGenerator));
            }
        }

        /// <returns>Date converted to seconds since Unix epoch (Jan 1, 1970, midnight UTC).</returns>
        private static long ToUnixEpochDate(DateTime date)
          => (long)Math.Round((date.ToUniversalTime() -
                               new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero))
                              .TotalSeconds);

        /// <summary>
        /// IMAGINE BIG RED WARNING SIGNS HERE!
        /// You'd want to retrieve claims through your claims provider
        /// in whatever way suits you, the below is purely for demo purposes!
        /// </summary>
        private static Task<ClaimsIdentity> GetClaimsIdentity(ApplicationUser user)
        {
            if (user.UserName == "admin" &&
                user.Password == "admin")
            {
                return Task.FromResult(new ClaimsIdentity(
                  new GenericIdentity(user.UserName, "Token"),new []
                  {
                      new Claim("IdONG","0"), 
                  }));
            }

            // Credentials are invalid, or account doesn't exist
            return Task.FromResult<ClaimsIdentity>(null);
        }
    }
}