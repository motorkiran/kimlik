// The browser side of Kimlik's passkey ceremonies. A form marked data-passkey="create" adds a passkey, and a button
// marked data-passkey-sign-in signs in with one: each fetches its options from data-options, has the authenticator
// answer them, and posts the answer in its form's Credential and State fields. Elements marked data-passkey-unsupported
// show when the browser cannot use passkeys; the pages work without this script, only without passkeys.
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

    async function fetchOptions(form, url) {
        const response = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { RequestVerificationToken: form.elements.__RequestVerificationToken.value },
        });

        if (!response.ok) {
            throw new Error(`Kimlik answered ${response.status}.`);
        }

        return response.json();
    }

    function post(form, credential, state, action) {
        form.elements.Credential.value = JSON.stringify(credential.toJSON());
        form.elements.State.value = state;
        if (action) {
            form.action = action;
        }

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
                const { options, state } = await fetchOptions(form, form.dataset.options);
                const credential = await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options) });
                post(form, credential, state);
            } catch (error) {
                report(form, error);
            }
        });
    }

    // A button marked data-passkey-sign-in signs in with a passkey through its form, posting to the button's formaction.
    for (const button of document.querySelectorAll('button[data-passkey-sign-in]')) {
        const form = button.form;
        let autofill = null;

        async function signIn(mediation) {
            const { options, state } = await fetchOptions(form, button.dataset.options);
            const request = { publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options) };
            if (mediation) {
                autofill = new AbortController();
                request.mediation = mediation;
                request.signal = autofill.signal;
            }

            post(form, await navigator.credentials.get(request), state, button.formAction);
        }

        button.hidden = false;
        button.addEventListener('click', async event => {
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
        if (button.dataset.autofill !== undefined && typeof PublicKeyCredential.isConditionalMediationAvailable === 'function') {
            PublicKeyCredential.isConditionalMediationAvailable()
                .then(available => available ? signIn('conditional') : undefined)
                .catch(error => report(form, error));
        }
    }
})();
