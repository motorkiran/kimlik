// The browser side of Kimlik's passkey ceremonies. A form marked data-passkey="create" adds a passkey, and one marked
// data-passkey="get" signs in with one: each fetches its options from data-options, has the authenticator answer
// them, and posts the answer in its Credential and State fields. Elements marked data-passkey-unsupported show when
// the browser cannot use passkeys; the pages work without this script, only without passkeys.
'use strict';

(() => {
    const supported = typeof window.PublicKeyCredential === 'function'
        && typeof PublicKeyCredential.parseCreationOptionsFromJSON === 'function'
        && typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

    for (const element of document.querySelectorAll('[data-passkey-unsupported]')) {
        element.hidden = supported;
    }

    if (!supported) {
        return;
    }

    async function fetchOptions(form) {
        const response = await fetch(form.dataset.options, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { RequestVerificationToken: form.elements.__RequestVerificationToken.value },
        });

        if (!response.ok) {
            throw new Error(`Kimlik answered ${response.status}.`);
        }

        return response.json();
    }

    function post(form, credential, state) {
        form.elements.Credential.value = JSON.stringify(credential.toJSON());
        form.elements.State.value = state;
        form.submit();
    }

    // The person closing the browser's dialog is not an error worth showing.
    function report(form, error) {
        if (error.name !== 'NotAllowedError' && error.name !== 'AbortError') {
            const message = form.querySelector('[data-passkey-error]');
            if (message) {
                message.hidden = false;
            }
        }
    }

    for (const form of document.querySelectorAll('form[data-passkey="create"]')) {
        form.hidden = false;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            try {
                const { options, state } = await fetchOptions(form);
                const credential = await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) });
                post(form, credential, state);
            } catch (error) {
                report(form, error);
            }
        });
    }

    for (const form of document.querySelectorAll('form[data-passkey="get"]')) {
        let autofill = null;

        async function signIn(mediation) {
            const { options, state } = await fetchOptions(form);
            const request = { publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) };
            if (mediation) {
                autofill = new AbortController();
                request.mediation = mediation;
                request.signal = autofill.signal;
            }

            post(form, await navigator.credentials.get(request), state);
        }

        form.hidden = false;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            // The browser runs one request at a time, so the button takes over from the address field's.
            autofill?.abort();
            try {
                await signIn(null);
            } catch (error) {
                report(form, error);
            }
        });

        // The address field offers the browser's passkeys as the person types (conditional mediation).
        if (form.dataset.autofill !== undefined && typeof PublicKeyCredential.isConditionalMediationAvailable === 'function') {
            PublicKeyCredential.isConditionalMediationAvailable()
                .then(available => available ? signIn('conditional') : undefined)
                .catch(error => report(form, error));
        }
    }
})();
