﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading.Tasks;
using SVX2;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace SVAuth
{
    public static class SVX2_Test_ImplicitFlow
    {
        static readonly Principal googlePrincipal = Principal.Of("Google");
        static readonly Principal mattMerchantPrincipal = Principal.Of("MattMerchant");

        static Principal GoogleUserPrincipal(string username)
        {
            return Principal.Of("Google:" + username);
        }

        public class IdTokenBody : SVX_MSG
        {
            // Corresponds to GoogleUserPrincipal(username)
            public string username;
            public Principal rpPrincipal;
        }

        public class GoogleIdTokenVerifier : MessagePayloadSecretGenerator<IdTokenBody>
        {
            // XXX Eventually this needs to be a parameter.
            protected override PrincipalHandle Signer => googlePrincipal;

            protected override PrincipalHandle[] GetReaders(object theParams)
            {
                var body = (IdTokenBody)theParams;
                return new Principal[] {
                    // Comment this to get an internal error during secret generation.
                    googlePrincipal,
                    // Comment either of these to see the secret export check fail.
                    body.rpPrincipal,
                    GoogleUserPrincipal(body.username),
                    // Uncomment to see the verification fail.
                    //Principal.Of("other")
                };
            }

            protected override IdTokenBody RawExtractUnverified(string secretValue)
            {
                throw new NotImplementedException();
            }

            protected override string RawGenerate(IdTokenBody theParams)
            {
                // Pretend we don't have Google's private key and can't generate.
                throw new NotImplementedException();
            }

            protected override IdTokenBody RawVerifyAndExtract(string secretValue)
            {
                return JsonConvert.DeserializeObject<IdTokenBody>(secretValue);
            }
        }

        class GoogleIdTokenGenerator : GoogleIdTokenVerifier
        {
            protected override string RawGenerate(IdTokenBody theParams)
            {
                // Nothing actually secret. :)
                return JsonConvert.SerializeObject(theParams);
            }
        }

        static MessageStructure<IdTokenRequest> idTokenRequestStructure;
        static MessageStructure<IdTokenResponse> idTokenResponseStructure;

        public class IdTokenRequest : SVX_MSG
        {
            public Principal rpPrincipal;
            public Secret state;
        }
        public class IdTokenResponse : SVX_MSG
        {
            public PayloadSecret<IdTokenBody> idToken;
            public Secret state;
        }

        public class IdPAuthenticationEntry : SVX_MSG
        {
            public PrincipalHandle authenticatedClient;
            public string googleUsername;
        }
        public class RPAuthenticationConclusion : SVX_MSG
        {
            public PrincipalHandle authenticatedClient;
            public string googleUsername;
        }

        public class Google_IdP : Participant
        {
            public Principal SVXPrincipal => googlePrincipal;
            private GoogleIdTokenGenerator idTokenGenerator = new GoogleIdTokenGenerator();

            private DeclarablePredicate<Principal /*underlying client*/, string /*username*/> SignedInPredicate
                = new DeclarablePredicate<Principal, string>();

            public string TokenEndpoint(PrincipalHandle client, string idTokenRequestStr)
            {
                var req = JsonConvert.DeserializeObject<IdTokenRequest>(idTokenRequestStr);
                idTokenRequestStructure.Import(req,
                    // We don't know who produced the request.
                    PrincipalFacet.GenerateNew(SVXPrincipal),
                    client);

                // In reality, AuthenticateClient couldn't be done
                // synchronously, so both TokenEndpoint and AuthenticateClient
                // would be broken into a start and a callback.
                var idpConc = AuthenticateClient(client);

                var resp = SVX_Ops.Call(SVX_MakeIdTokenResponse, req, idpConc);

                idTokenResponseStructure.Export(resp, client, req.rpPrincipal);
                return JsonConvert.SerializeObject(resp);
            }
            
            IdPAuthenticationEntry AuthenticateClient(PrincipalHandle client)
            {
                // In reality, once the user logs in, we would store the
                // IdPAuthenticationEntry in the session data structure of the
                // web application framework.
                var username = "Alice";
                return SVX_Ops.Call(SVX_ConcludeClientAuthentication, new IdPAuthenticationEntry {
                    authenticatedClient = client,
                    googleUsername = username,
                });
            }

            // Write lambda by hand because all compiler-generated classes are
            // currently excluded from decompilation of method bodies by CCI.
            class SignedInDeclarer
            {
                internal Google_IdP outer;
                internal IdPAuthenticationEntry entry;
                internal void Declare()
                {
                    outer.SignedInPredicate.Declare(VProgram_API.UnderlyingPrincipal(entry.authenticatedClient), entry.googleUsername);
                }
            }

            public IdPAuthenticationEntry SVX_ConcludeClientAuthentication(IdPAuthenticationEntry entry)
            {
                var d = new SignedInDeclarer { outer = this, entry = entry };
                SVX_Ops.Ghost(d.Declare);
                VProgram_API.AssumeActsFor(entry.authenticatedClient, GoogleUserPrincipal(entry.googleUsername));
                // Reuse the message... Should be able to get away with it.
                return entry;
            }

            public IdTokenResponse SVX_MakeIdTokenResponse(IdTokenRequest req, IdPAuthenticationEntry idpConc)
            {
                // In TokenEndpoint, we requested an IdPAuthenticationEntry for
                // req.SVX_sender, but SVX doesn't know that, so we have to do a
                // concrete check.
                VProgram_API.Assert(req.SVX_sender == idpConc.authenticatedClient);

                // Put this in a separate SVX method so "body" gets an active SymT.
                var body = SVX_Ops.Call(SVX_MakeIdTokenBody, req, idpConc);
                return new IdTokenResponse
                {
                    idToken = idTokenGenerator.Generate(body, SVXPrincipal),  // sign the token
                    state = req.state
                };
            }

            public IdTokenBody SVX_MakeIdTokenBody(IdTokenRequest req, IdPAuthenticationEntry idpConc)
            {
                return new IdTokenBody
                {
                    rpPrincipal = req.rpPrincipal,
                    username = idpConc.googleUsername
                };
            }

            public bool Ghost_CheckSignedIn(Principal underlyingPrincipal, string username)
            {
                return SignedInPredicate.Check(underlyingPrincipal, username);
            }
        }

        class StateParams
        {
            // We expect this to be a facet issued by the RP.
            internal PrincipalHandle client;
        }
        class MattMerchant_Google_StateGenerator : SecretGenerator<StateParams>
        {
            protected override PrincipalHandle[] GetReaders(object theParams)
            {
                var params2 = (StateParams)theParams;
                return new PrincipalHandle[] { googlePrincipal, mattMerchantPrincipal, params2.client };
            }

            protected override string RawGenerate(StateParams theParams)
            {
                return "mattmerchant_google_state:" + theParams.client;
            }

            protected override void RawVerify(StateParams theParams, string secretValue)
            {
                if (secretValue != RawGenerate(theParams))
                    throw new ArgumentException();
            }
        }

        public class MattMerchant_RP : Participant
        {
            // XXX Definitely needs to be a parameter.
            public Principal SVXPrincipal => mattMerchantPrincipal;

            MattMerchant_Google_StateGenerator stateGenerator = new MattMerchant_Google_StateGenerator();

            // Something here is tripping up BCT.  Just exclude it until we have
            // fully as-needed translation.
            [BCTOmitImplementation]
            public string LoginStart(PrincipalHandle client)
            {
                var req = new IdTokenRequest
                {
                    rpPrincipal = SVXPrincipal,
                    state = stateGenerator.Generate(
                        new StateParams { client = client },
                        SVXPrincipal)
                };
                idTokenRequestStructure.Export(req, client, googlePrincipal);
                return JsonConvert.SerializeObject(req);
            }

            public void LoginCallback(PrincipalHandle client, string idTokenResponseStr)
            {
                var idTokenResponse = JsonConvert.DeserializeObject<IdTokenResponse>(idTokenResponseStr);
                idTokenResponseStructure.Import(idTokenResponse,
                    // We don't know who produced the redirection.
                    PrincipalFacet.GenerateNew(SVXPrincipal),
                    client);

                var conc = SVX_Ops.Call(SVX_SignInRP, idTokenResponse);

                SVX_Ops.Certify(conc, LoginSafety);
                SVX_Ops.Certify(conc, LoginXSRFPrevention, Tuple.Create(googlePrincipal, typeof(Google_IdP)));
                // AbandonAndCreateSession...
            }
            public RPAuthenticationConclusion SVX_SignInRP(IdTokenResponse resp)
            {
                // Comment out these 2 lines to see the verification fail.
                if (resp.idToken.theParams.rpPrincipal != SVXPrincipal)
                    throw new Exception("IdTokenResponse was not issued to this RP.");
                var expectedStateGeneratorType = typeof(MattMerchant_Google_StateGenerator);
                // Pull new { ... } out to work around BCT mistranslation.
                var stateParams = new StateParams { client = resp.SVX_sender };
                stateGenerator.Verify(stateParams, resp.state);
                return new RPAuthenticationConclusion {
                    authenticatedClient = resp.SVX_sender,
                    googleUsername = resp.idToken.theParams.username
                };
            }

            public static bool LoginSafety(RPAuthenticationConclusion conc)
            {
                var googleUser = GoogleUserPrincipal(conc.googleUsername);
                VProgram_API.AssumeTrustedServer(googlePrincipal);
                VProgram_API.AssumeTrustedServer(mattMerchantPrincipal);
                VProgram_API.AssumeTrusted(googleUser);

                return VProgram_API.ActsFor(conc.authenticatedClient, googleUser);
            }

            public static bool LoginXSRFPrevention(RPAuthenticationConclusion conc)
            {
                VProgram_API.AssumeTrustedServer(googlePrincipal);
                VProgram_API.AssumeTrustedServer(mattMerchantPrincipal);
                VProgram_API.AssumeTrustedBrowser(conc.authenticatedClient);

                var idp = VProgram_API.GetParticipant<Google_IdP>(googlePrincipal);
                return idp.Ghost_CheckSignedIn(VProgram_API.UnderlyingPrincipal(conc.authenticatedClient), conc.googleUsername);
            }
        }

        [BCTOmitImplementation]
        static void InitializeMessageStructures()
        {
            idTokenRequestStructure = new MessageStructure<IdTokenRequest>() { BrowserOnly = true };
            idTokenRequestStructure.AddSecret(nameof(IdTokenRequest.state),
                // The sender (the client) gets implicitly added.
                // It doesn't matter whether we add the IdP here.  Convention?
                (msg) => new PrincipalHandle[] { msg.rpPrincipal });

            idTokenResponseStructure = new MessageStructure<IdTokenResponse>() { BrowserOnly = true };
            idTokenResponseStructure.AddSecret(nameof(IdTokenRequest.state),
                // We're not passing the state along further, so we don't have to declare any readers.
                (msg) => new PrincipalHandle[] { });
            idTokenResponseStructure.AddMessagePayloadSecret(nameof(IdTokenResponse.idToken),
                (msg) => new PrincipalHandle[] { },
                new GoogleIdTokenVerifier(),
                true);
        }

        // Do not BCTOmitImplementation this; it affects some static field initializers that we want.
        static SVX2_Test_ImplicitFlow()
        {
            InitializeMessageStructures();
        }

        [BCTOmitImplementation]
        public static void Test()
        {
            var idp = new Google_IdP();
            var rp = new MattMerchant_RP();

            var aliceIdP = PrincipalFacet.GenerateNew(googlePrincipal);
            var aliceRP = PrincipalFacet.GenerateNew(mattMerchantPrincipal);

            var idTokenRequestStr = rp.LoginStart(aliceRP);
            var idTokenResponseStr = idp.TokenEndpoint(aliceIdP, idTokenRequestStr);
            rp.LoginCallback(aliceRP, idTokenResponseStr);
        }
    }
}
