class InteropApi {
    constructor() {
    	return new Proxy(this, {
    		get(target, prop) {
				if (WINDOWS) {
					return undefined;
				}
				// If the property is not a method of InteropApi, 
				// treat it as a .NET class name
				if (typeof prop === 'string' && !target[prop]) {
					return new Proxy({}, {
						get(_, methodName) {
							// Return a method that calls the .NET method dynamically
							return async (...args) => {
								return await target.callMethod(prop, methodName, ...args);
							};
						}
					});
				}
				return target[prop];
    		}
    	});
    }
  
    async callMethod(className, methodName, ...args) {
			if (WEB) {
				let password = localStorage.getItem('VRCX_PASSWORD');
				if (!password) {
					password = prompt('Enter VRCX password:').trim();
				}
				localStorage.setItem('VRCX_PASSWORD', password);

				const response = await fetch('/', {
					method: 'POST',
					headers: {
						'Content-Type': 'application/json',
						'authorization': `Bearer ${password}`
					},
					body: JSON.stringify({
						className,
						methodName,
						args: args.map(arg => {
							if (typeof arg === 'object' && arg instanceof Map) {
								return { ...Object.fromEntries(arg), __is_map__: true };
							}
							return arg;
						})
					})
				});
				if (response.status === 401) {
					alert('Unauthorized. Please check your password.');
					localStorage.removeItem('VRCX_PASSWORD');
					return;
				}
				if (!response.ok) {
					throw new Error(`HTTP error! status: ${response.status}`);
				}
				const data = await response.json();
				if (data.status !== 'success') {
					throw new Error(`Error calling ${className}.${methodName}: ${data.error}`);
				}
				return data.result;
			}
    	return window.interopApi.callDotNetMethod(className, methodName, args)
    		.then(result => {
    			return result;
    	});
    }
}

export default new InteropApi();